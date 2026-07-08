using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoInferenceDemo.ImageInspection.Services;

namespace VideoInferenceDemo.ImageInspection.Camera;

public sealed partial class InspectionCameraConfigViewModel : ObservableObject
{
    private readonly string _configPath;
    private readonly CameraProviderRegistry _cameraProviders;
    private readonly IFolderPickerService? _folderPicker;

    public InspectionCameraConfigViewModel(
        string configPath,
        InspectionCameraSettings settings,
        CameraProviderRegistry cameraProviders,
        IFolderPickerService? folderPicker = null)
    {
        _configPath = configPath;
        _cameraProviders = cameraProviders;
        _folderPicker = folderPicker;
        CameraProviders = _cameraProviders.DescribeProviders();
        foreach (var camera in settings.Cameras)
        {
            Cameras.Add(new InspectionCameraProfileViewModel(camera, _cameraProviders));
        }

        if (Cameras.Count == 0)
        {
            Cameras.Add(new InspectionCameraProfileViewModel(InspectionCameraProfile.CreateDefault(1), _cameraProviders));
        }

        SelectedCamera = Cameras.FirstOrDefault(item => item.Id == settings.SelectedCameraId) ?? Cameras[0];
    }

    public ObservableCollection<InspectionCameraProfileViewModel> Cameras { get; } = [];

    public IReadOnlyList<CameraProviderDescriptor> CameraProviders { get; }

    [ObservableProperty] private InspectionCameraProfileViewModel? selectedCamera;
    [ObservableProperty] private string errorMessage = string.Empty;
    [ObservableProperty] private bool saveSucceeded;

    [RelayCommand]
    private void AddCamera()
    {
        var camera = new InspectionCameraProfileViewModel(InspectionCameraProfile.CreateDefault(Cameras.Count + 1), _cameraProviders);
        Cameras.Add(camera);
        SelectedCamera = camera;
        RemoveCameraCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRemoveCamera))]
    private void RemoveCamera()
    {
        if (SelectedCamera == null || Cameras.Count <= 1)
        {
            return;
        }

        var selected = SelectedCamera;
        var index = Cameras.IndexOf(selected);
        if (index < 0)
        {
            SelectedCamera = Cameras[0];
            return;
        }

        var replacementIndex = index >= Cameras.Count - 1 ? index - 1 : index + 1;
        SelectedCamera = Cameras[replacementIndex];
        Cameras.RemoveAt(index);
        RemoveCameraCommand.NotifyCanExecuteChanged();
    }

    private bool CanRemoveCamera() => SelectedCamera != null && Cameras.Count > 1;

    partial void OnSelectedCameraChanged(InspectionCameraProfileViewModel? value)
    {
        RemoveCameraCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void BrowseImageSaveDirectory()
    {
        if (SelectedCamera == null || _folderPicker == null)
        {
            return;
        }

        var selectedPath = _folderPicker.PickFolder("选择图片保存目录", SelectedCamera.ImageSaveDirectory);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            SelectedCamera.ImageSaveDirectory = selectedPath;
        }
    }

    [RelayCommand]
    private void Save()
    {
        SaveSucceeded = TrySave();
    }

    public bool TrySave()
    {
        var profiles = new List<InspectionCameraProfile>();
        for (var index = 0; index < Cameras.Count; index++)
        {
            profiles.Add(Cameras[index].Build().Normalize(index + 1));
        }

        if (profiles.Count == 0)
        {
            profiles.Add(InspectionCameraProfile.CreateDefault(1));
        }

        InspectionCameraSettingsStorage.Save(
            _configPath,
            new InspectionCameraSettings
            {
                Cameras = profiles,
                SelectedCameraId = SelectedCamera?.Id ?? profiles[0].Id
            });

        return true;
    }
}
