using System.Windows;
using Microsoft.Win32;
using VideoInferenceDemo.ImageInspection.Camera;
using VideoInferenceDemo.ImageInspection.Roi;
using VideoInferenceDemo.ImageInspection.Settings;
using VideoInferenceDemo.ImageInspection.Tasks;

namespace VideoInferenceDemo.ImageInspection.Services;

public sealed class ImageInspectionDialogService : IImageInspectionDialogService, IImageFilePicker
{
    private readonly Window _owner;
    private readonly CameraProviderRegistry _cameraProviders;
    private PersonnelManagementWindow? _personnelWindow;

    public ImageInspectionDialogService(Window owner, CameraProviderRegistry cameraProviders)
    {
        _owner = owner;
        _cameraProviders = cameraProviders;
    }

    public void ShowCameraSettings()
    {
        var settings = InspectionCameraSettingsStorage.Load(InspectionConfigPaths.CameraSettingsPath);
        var window = new InspectionCameraConfigWindow(
            new InspectionCameraConfigViewModel(
                InspectionConfigPaths.CameraSettingsPath,
                settings,
                _cameraProviders,
                new WpfFolderPickerService(_owner)))
        {
            Owner = _owner
        };
        window.ShowDialog();
    }

    public void ShowTaskSettings()
    {
        var cameraSettings = InspectionCameraSettingsStorage.Load(InspectionConfigPaths.CameraSettingsPath);
        var recipeCatalog = InspectionRecipeCatalogStorage.Load(InspectionConfigPaths.RecipeCatalogPath);
        var settings = InspectionTaskSettingsStorage.Load(
            InspectionConfigPaths.TaskSettingsPath,
            cameraSettings.Cameras,
            recipeCatalog.Recipes.FirstOrDefault());
        var modelSettings = InspectionModelSettingsStorage.Load(InspectionConfigPaths.ModelSettingsPath);
        var window = new InspectionTaskConfigWindow(
            new InspectionTaskConfigViewModel(
                InspectionConfigPaths.TaskSettingsPath,
                InspectionConfigPaths.ModelSettingsPath,
                settings,
                modelSettings,
                cameraSettings.Cameras))
        {
            Owner = _owner
        };
        window.ShowDialog();
    }

    public bool ShowRoiSettings(
        string productModel,
        string taskId,
        string positionNo,
        IReadOnlyList<InspectionCameraProfile> cameras)
    {
        var catalog = InspectionRecipeCatalogStorage.Load(InspectionConfigPaths.RecipeCatalogPath);
        var modelSettings = InspectionModelSettingsStorage.Load(InspectionConfigPaths.ModelSettingsPath);
        var window = new InspectionRoiConfigWindow(
            new InspectionRoiConfigWindowViewModel(
                InspectionConfigPaths.RecipeCatalogPath,
                productModel,
                taskId,
                positionNo,
                catalog,
                modelSettings,
                cameras,
                this))
        {
            Owner = _owner
        };

        return window.ShowDialog() == true;
    }

    public bool ShowParameterSettings()
    {
        var settings = InspectionParameterSettingsStorage.Load(InspectionConfigPaths.ParameterSettingsPath);
        var window = new InspectionParameterSettingsWindow(
            new InspectionParameterSettingsViewModel(InspectionConfigPaths.ParameterSettingsPath, settings))
        {
            Owner = _owner
        };
        return window.ShowDialog() == true;
    }

    public void ShowPersonnelManagement(PersonnelManagementViewModel viewModel)
    {
        var window = new PersonnelManagementWindow(viewModel)
        {
            Owner = _owner
        };
        _personnelWindow = window;
        try
        {
            window.ShowDialog();
        }
        finally
        {
            if (ReferenceEquals(_personnelWindow, window))
            {
                _personnelWindow = null;
            }
        }
    }

    public bool ShowLogin(PersonnelAuthenticationService authenticationService)
    {
        var window = new LoginWindow(authenticationService)
        {
            Owner = _owner
        };

        return window.ShowDialog() == true;
    }

    public bool ConfirmAdminPassword(PersonnelRepository personnelRepository)
    {
        return AdminPasswordWindow.Confirm(_owner, personnelRepository);
    }

    public string? RequestPersonnelPassword(PersonnelEditorItem item)
    {
        return PasswordChangeWindow.Request(_personnelWindow ?? _owner, item);
    }

    public string? PickImageFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog(_owner) == true ? dialog.FileName : null;
    }
}
