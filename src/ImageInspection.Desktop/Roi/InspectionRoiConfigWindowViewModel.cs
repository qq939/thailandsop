using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageBox;
using VideoInferenceDemo.ImageInspection.Services;

namespace VideoInferenceDemo.ImageInspection.Roi;

#pragma warning disable CA1416
public sealed partial class InspectionRoiConfigWindowViewModel : ObservableObject
{
    private readonly string _catalogPath;
    private readonly InspectionRecipeCatalog _catalog;
    private readonly IImageFilePicker _imageFilePicker;
    private readonly Dictionary<string, List<InspectionRoiConfig>> _roisByCameraId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _referenceImagesByCameraId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, InspectionCameraAlignmentConfig> _alignmentByCameraId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, InspectionModelConfig> _modelsById = new(StringComparer.OrdinalIgnoreCase);
    private bool _isSwitchingContext;
    private bool _isSwitchingCamera;

    public InspectionRoiConfigWindowViewModel(
        string catalogPath,
        string productModel,
        string taskId,
        string positionNo,
        InspectionRecipeCatalog catalog,
        InspectionModelSettings modelSettings,
        IReadOnlyList<InspectionCameraProfile> cameras,
        IImageFilePicker imageFilePicker)
    {
        _catalogPath = catalogPath;
        _catalog = catalog;
        _imageFilePicker = imageFilePicker;
        foreach (var model in modelSettings.Models.Where(model => model.Enabled).OrderBy(model => model.Name))
        {
            _modelsById[model.Id] = model;
            if (model.TaskType != ModelTaskType.PresenceClassification)
            {
                AvailableModels.Add(new InspectionModelOptionViewModel(model.Id, model.Name, model.TaskType));
            }

            if (model.TaskType == ModelTaskType.ObbDetection)
            {
                AvailableLocatorModels.Add(new InspectionModelOptionViewModel(model.Id, model.Name, model.TaskType));
            }
        }

        foreach (var camera in cameras
                     .Where(camera => !string.IsNullOrWhiteSpace(camera.Id))
                     .GroupBy(camera => camera.Id, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            AvailableCameras.Add(new InspectionRoiCameraOptionViewModel(camera.Id, camera.Name));
        }

        if (AvailableCameras.Count == 0)
        {
            throw new InvalidOperationException("ROI settings require at least one bound camera.");
        }

        SelectedCamera = AvailableCameras[0];

        EnsureContext(taskId, productModel, positionNo);
        SelectedContext = Contexts.FirstOrDefault(context => context.Matches(taskId, productModel, positionNo))
                          ?? Contexts.First();
    }

    public ObservableCollection<InspectionRoiContextViewModel> Contexts { get; } = [];

    public ObservableCollection<InspectionRoiConfigViewModel> Rois { get; } = [];

    public ObservableCollection<ImageOverlayItem> OverlayItems { get; } = [];

    public ObservableCollection<InspectionModelOptionViewModel> AvailableModels { get; } = [];

    public ObservableCollection<InspectionModelOptionViewModel> AvailableLocatorModels { get; } = [];

    public ObservableCollection<InspectionModelClassOptionViewModel> AvailableLocatorClasses { get; } = [];

    public ObservableCollection<InspectionRoiCameraOptionViewModel> AvailableCameras { get; } = [];

    public bool CanSwitchContext => Contexts.Count > 1;

    [ObservableProperty] private InspectionRoiContextViewModel? selectedContext;
    [ObservableProperty] private InspectionRoiCameraOptionViewModel? selectedCamera;
    [ObservableProperty] private InspectionRoiConfigViewModel? selectedRoi;
    [ObservableProperty] private InspectionCameraAlignmentConfigViewModel? selectedAlignment;
    [ObservableProperty] private InspectionModelClassOptionViewModel? selectedLocatorClass;
    [ObservableProperty] private string referenceImagePath = string.Empty;
    [ObservableProperty] private ImageSource? previewImage;
    [ObservableProperty] private bool hasReferenceImage;
    [ObservableProperty] private string coordinateHintText = "Load a reference image to edit ROI coordinates in pixels.";
    [ObservableProperty] private bool saveSucceeded;

    partial void OnSelectedContextChanging(InspectionRoiContextViewModel? oldValue, InspectionRoiContextViewModel? newValue)
    {
        if (_isSwitchingContext || oldValue == null)
        {
            return;
        }

        SaveRecipe(oldValue);
    }

    partial void OnSelectedContextChanged(InspectionRoiContextViewModel? value)
    {
        if (_isSwitchingContext || value == null)
        {
            return;
        }

        _isSwitchingContext = true;
        LoadContext(value);
        _isSwitchingContext = false;
    }

    partial void OnSelectedCameraChanging(InspectionRoiCameraOptionViewModel? oldValue, InspectionRoiCameraOptionViewModel? newValue)
    {
        if (_isSwitchingContext || _isSwitchingCamera || oldValue == null)
        {
            return;
        }

        SaveCameraState(oldValue.Id);
    }

    partial void OnSelectedCameraChanged(InspectionRoiCameraOptionViewModel? value)
    {
        if (_isSwitchingContext || _isSwitchingCamera || value == null)
        {
            return;
        }

        _isSwitchingCamera = true;
        LoadCameraState(value.Id);
        _isSwitchingCamera = false;
    }

    partial void OnSelectedRoiChanged(InspectionRoiConfigViewModel? value)
    {
        RefreshOverlayItems();
        RemoveRoiCommand.NotifyCanExecuteChanged();
        DuplicateRoiCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedAlignmentChanged(InspectionCameraAlignmentConfigViewModel? oldValue, InspectionCameraAlignmentConfigViewModel? newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnAlignmentPropertyChanged;
        }

        if (newValue != null)
        {
            ApplyCoordinateSpace(newValue);
            newValue.PropertyChanged += OnAlignmentPropertyChanged;
        }

        RefreshLocatorClasses();
        RefreshOverlayItems();
    }

    partial void OnSelectedLocatorClassChanged(InspectionModelClassOptionViewModel? value)
    {
        if (SelectedAlignment == null || value == null)
        {
            return;
        }

        SelectedAlignment.LocatorClassId = value.Id;
        SelectedAlignment.LocatorClassName = value.Name;
    }

    partial void OnReferenceImagePathChanged(string value)
    {
        LoadReferenceImage(value);
    }

    [RelayCommand]
    private void AddRoi()
    {
        if (SelectedCamera == null)
        {
            return;
        }

        var nextIndex = GetNextRoiOrdinal();
        var roi = new InspectionRoiConfigViewModel(new InspectionRoiConfig
        {
            Id = $"roi-{nextIndex}",
            Name = $"ROI-{nextIndex}",
            CameraId = SelectedCamera.Id,
            SortOrder = nextIndex,
            ModelId = AvailableModels.FirstOrDefault()?.Id ?? string.Empty
        });

        AddRoiInternal(roi);
        SelectedRoi = roi;
        RefreshOverlayItems();
    }

    [RelayCommand(CanExecute = nameof(CanEditSelectedRoi))]
    private void RemoveRoi()
    {
        if (SelectedRoi == null)
        {
            return;
        }

        var index = Rois.IndexOf(SelectedRoi);
        SelectedRoi.PropertyChanged -= OnRoiPropertyChanged;
        Rois.Remove(SelectedRoi);
        SelectedRoi = Rois.Count == 0 ? null : Rois[Math.Max(0, System.Math.Min(index, Rois.Count - 1))];
        RefreshOverlayItems();
    }

    [RelayCommand(CanExecute = nameof(CanEditSelectedRoi))]
    private void DuplicateRoi()
    {
        if (SelectedRoi == null)
        {
            return;
        }

        var copy = new InspectionRoiConfigViewModel(SelectedRoi.Build())
        {
            Id = $"{SelectedRoi.Id}-copy",
            Name = $"{SelectedRoi.Name}-copy",
            CameraId = SelectedCamera?.Id ?? SelectedRoi.CameraId,
            SortOrder = Rois.Count + 1
        };

        AddRoiInternal(copy);
        SelectedRoi = copy;
        RefreshOverlayItems();
    }

    private bool CanEditSelectedRoi() => SelectedRoi != null;

    [RelayCommand]
    private void Save()
    {
        SaveSucceeded = TrySave();
    }

    [RelayCommand]
    private void PickReferenceImage()
    {
        var imagePath = _imageFilePicker.PickImageFile();
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            ReferenceImagePath = imagePath;
        }
    }

    [RelayCommand]
    private void ClearReferenceImage()
    {
        ReferenceImagePath = string.Empty;
        PreviewImage = null;
    }

    public bool TrySave()
    {
        SaveCurrentRecipe();
        InspectionRecipeCatalogStorage.Save(_catalogPath, _catalog);
        return true;
    }

    private void SaveCurrentRecipe()
    {
        if (SelectedContext == null)
        {
            return;
        }

        SaveRecipe(SelectedContext);
    }

    private void SaveRecipe(InspectionRoiContextViewModel context)
    {
        if (SelectedCamera != null)
        {
            SaveCameraState(SelectedCamera.Id);
        }

        var recipe = _catalog.GetOrCreate(context.ProductModel, context.TaskId, context.PositionNo);
        recipe.ReferenceImagePathsByCameraId ??= [];
        recipe.ReferenceImagePathsByCameraId.Clear();
        recipe.AlignmentByCameraId ??= [];
        recipe.AlignmentByCameraId.Clear();

        foreach (var camera in AvailableCameras)
        {
            recipe.ReferenceImagePathsByCameraId[camera.Id] =
                _referenceImagesByCameraId.TryGetValue(camera.Id, out var path) ? path : string.Empty;
            recipe.AlignmentByCameraId[camera.Id] =
                _alignmentByCameraId.TryGetValue(camera.Id, out var alignment)
                    ? alignment.Normalize()
                    : new InspectionCameraAlignmentConfig();
        }

        recipe.ReferenceImagePath = recipe.ReferenceImagePathsByCameraId.Values
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path)) ?? string.Empty;
        recipe.Rois = AvailableCameras
            .SelectMany(camera =>
            {
                if (!_roisByCameraId.TryGetValue(camera.Id, out var rois))
                {
                    return Enumerable.Empty<InspectionRoiConfig>();
                }

                return rois.Select((roi, index) =>
                {
                    var clone = CloneRoi(roi);
                    clone.CameraId = camera.Id;
                    clone.SortOrder = index + 1;
                    return clone;
                });
            })
            .ToList();
    }

    public void RefreshOverlayItems()
    {
        OverlayItems.Clear();
        var imageWidth = PreviewImage?.Width ?? 0;
        var imageHeight = PreviewImage?.Height ?? 0;
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return;
        }

        for (var index = 0; index < Rois.Count; index++)
        {
            var isSelected = SelectedRoi == Rois[index];
            OverlayItems.Add(Rois[index].ToOverlay(imageWidth, imageHeight, isSelected));
            OverlayItems.Add(Rois[index].ToTextOverlay(imageWidth, imageHeight, isSelected));
        }

        if (SelectedAlignment?.Enabled == true)
        {
            OverlayItems.Add(SelectedAlignment.ToOverlay(imageWidth, imageHeight));
            OverlayItems.Add(SelectedAlignment.ToTextOverlay(imageWidth, imageHeight));
        }
    }

    private void AddRoiInternal(InspectionRoiConfigViewModel roi)
    {
        ApplyCoordinateSpace(roi);
        roi.PropertyChanged += OnRoiPropertyChanged;
        Rois.Add(roi);
    }

    private void LoadContext(InspectionRoiContextViewModel context)
    {
        var recipe = _catalog.GetOrCreate(context.ProductModel, context.TaskId, context.PositionNo);
        InspectionRecipeCameraBinding.NormalizeForCameraIds(recipe, AvailableCameras.Select(camera => camera.Id).ToArray());
        _roisByCameraId.Clear();
        _referenceImagesByCameraId.Clear();
        _alignmentByCameraId.Clear();

        foreach (var camera in AvailableCameras)
        {
            _referenceImagesByCameraId[camera.Id] = InspectionRecipeCameraBinding.GetReferenceImagePath(recipe, camera.Id);
            _alignmentByCameraId[camera.Id] = recipe.AlignmentByCameraId.TryGetValue(camera.Id, out var alignment)
                ? alignment.Normalize()
                : new InspectionCameraAlignmentConfig();
            _roisByCameraId[camera.Id] = recipe.Rois
                .Where(roi => string.Equals(roi.CameraId, camera.Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(roi => roi.SortOrder)
                .Select(CloneRoi)
                .ToList();
        }

        var selectedCamera = SelectedCamera == null
            ? AvailableCameras[0]
            : AvailableCameras.FirstOrDefault(camera => string.Equals(camera.Id, SelectedCamera.Id, StringComparison.OrdinalIgnoreCase))
              ?? AvailableCameras[0];
        SelectedCamera = selectedCamera;

        if (_roisByCameraId.Values.Sum(items => items.Count) == 0)
        {
            _roisByCameraId[selectedCamera.Id].Add(new InspectionRoiConfig
            {
                Id = "roi-1",
                Name = "ROI-1",
                CameraId = selectedCamera.Id,
                SortOrder = 1,
                ModelId = AvailableModels.FirstOrDefault()?.Id ?? string.Empty
            });
        }

        LoadCameraState(selectedCamera.Id);
    }

    private void EnsureContext(string taskId, string productModel, string positionNo)
    {
        if (Contexts.Any(context => context.Matches(taskId, productModel, positionNo)))
        {
            return;
        }

        Contexts.Add(new InspectionRoiContextViewModel(taskId, productModel, positionNo));
    }

    private void OnRoiPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshOverlayItems();
    }

    private void OnAlignmentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InspectionCameraAlignmentConfigViewModel.LocatorModelId))
        {
            RefreshLocatorClasses();
        }

        RefreshOverlayItems();
    }

    private void RefreshLocatorClasses()
    {
        AvailableLocatorClasses.Clear();
        var modelId = SelectedAlignment?.LocatorModelId;
        if (string.IsNullOrWhiteSpace(modelId) || !_modelsById.TryGetValue(modelId, out var model))
        {
            SelectedLocatorClass = null;
            return;
        }

        for (var index = 0; index < model.Classes.Count; index++)
        {
            AvailableLocatorClasses.Add(new InspectionModelClassOptionViewModel(index, model.Classes[index]));
        }

        if (SelectedAlignment == null)
        {
            SelectedLocatorClass = null;
            return;
        }

        SelectedLocatorClass = AvailableLocatorClasses.FirstOrDefault(item =>
                                   item.Id == SelectedAlignment.LocatorClassId ||
                                   (!string.IsNullOrWhiteSpace(SelectedAlignment.LocatorClassName) &&
                                    string.Equals(item.Name, SelectedAlignment.LocatorClassName, StringComparison.OrdinalIgnoreCase)))
                               ?? AvailableLocatorClasses.FirstOrDefault();
    }

    private void SaveCameraState(string cameraId)
    {
        if (string.IsNullOrWhiteSpace(cameraId))
        {
            return;
        }

        _referenceImagesByCameraId[cameraId] = ReferenceImagePath?.Trim() ?? string.Empty;
        _alignmentByCameraId[cameraId] = SelectedAlignment?.Build() ?? new InspectionCameraAlignmentConfig();
        _roisByCameraId[cameraId] = Rois
            .Select((roi, index) =>
            {
                var built = roi.Build();
                built.CameraId = cameraId;
                built.SortOrder = index + 1;
                return built;
            })
            .ToList();
    }

    private void LoadCameraState(string cameraId)
    {
        foreach (var roi in Rois)
        {
            roi.PropertyChanged -= OnRoiPropertyChanged;
        }

        Rois.Clear();
        SelectedAlignment = new InspectionCameraAlignmentConfigViewModel(
            _alignmentByCameraId.TryGetValue(cameraId, out var alignment)
                ? alignment
                : new InspectionCameraAlignmentConfig());
        ApplyCoordinateSpace(SelectedAlignment);
        if (_roisByCameraId.TryGetValue(cameraId, out var rois))
        {
            foreach (var roi in rois.OrderBy(item => item.SortOrder))
            {
                var clone = CloneRoi(roi);
                clone.CameraId = cameraId;
                AddRoiInternal(new InspectionRoiConfigViewModel(clone));
            }
        }

        SelectedRoi = Rois.FirstOrDefault();
        ReferenceImagePath = _referenceImagesByCameraId.TryGetValue(cameraId, out var path)
            ? path
            : string.Empty;
        LoadReferenceImage(ReferenceImagePath);
        RefreshOverlayItems();
    }

    private void LoadReferenceImage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            PreviewImage = null;
            HasReferenceImage = false;
            CoordinateHintText = "Load a reference image to edit ROI coordinates in pixels.";
            ApplyCoordinateSpaceToEditors(0, 0);
            RefreshOverlayItems();
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            PreviewImage = bitmap;
            HasReferenceImage = true;
            CoordinateHintText = $"Pixel coordinates, reference image {bitmap.Width:0.#} x {bitmap.Height:0.#}.";
            ApplyCoordinateSpaceToEditors(bitmap.Width, bitmap.Height);
            RefreshOverlayItems();
        }
        catch
        {
            PreviewImage = null;
            HasReferenceImage = false;
            CoordinateHintText = "Load a reference image to edit ROI coordinates in pixels.";
            ApplyCoordinateSpaceToEditors(0, 0);
            RefreshOverlayItems();
        }
    }

    private void ApplyCoordinateSpaceToEditors(double imageWidth, double imageHeight)
    {
        foreach (var roi in Rois)
        {
            roi.SetCoordinateSpace(imageWidth, imageHeight);
        }

        SelectedAlignment?.SetCoordinateSpace(imageWidth, imageHeight);
    }

    private void ApplyCoordinateSpace(InspectionRoiConfigViewModel roi)
    {
        var imageWidth = PreviewImage?.Width ?? 0;
        var imageHeight = PreviewImage?.Height ?? 0;
        roi.SetCoordinateSpace(imageWidth, imageHeight);
    }

    private void ApplyCoordinateSpace(InspectionCameraAlignmentConfigViewModel? alignment)
    {
        if (alignment == null)
        {
            return;
        }

        var imageWidth = PreviewImage?.Width ?? 0;
        var imageHeight = PreviewImage?.Height ?? 0;
        alignment.SetCoordinateSpace(imageWidth, imageHeight);
    }

    private int GetNextRoiOrdinal()
    {
        var selectedCameraId = SelectedCamera?.Id ?? string.Empty;
        var otherCameraCount = _roisByCameraId
            .Where(item => !string.Equals(item.Key, selectedCameraId, StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.Value.Count);
        return otherCameraCount + Rois.Count + 1;
    }

    private static InspectionRoiConfig CloneRoi(InspectionRoiConfig roi)
    {
        return new InspectionRoiConfig
        {
            Id = roi.Id,
            Name = roi.Name,
            Enabled = roi.Enabled,
            CameraId = roi.CameraId,
            CenterX = roi.CenterX,
            CenterY = roi.CenterY,
            Width = roi.Width,
            Height = roi.Height,
            AngleDeg = roi.AngleDeg,
            ModelId = roi.ModelId,
            SortOrder = roi.SortOrder
        };
    }
}
