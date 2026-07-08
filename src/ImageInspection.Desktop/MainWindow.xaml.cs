using System.Windows;
using System.Runtime.Versioning;
using VideoInferenceDemo.ImageInspection.Services;

namespace VideoInferenceDemo.ImageInspection;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    [SupportedOSPlatform("windows7.0")]
    public MainWindow(PersonnelAuthenticationService authenticationService, WorkspaceDatabasePaths databasePaths)
    {
        InitializeComponent();

        var cameraProviders = WindowsCameraProviderRegistry.CreateDefault();
        var dialogs = new ImageInspectionDialogService(this, cameraProviders);
        _viewModel = MainViewModel.CreateRuntime(dialogs, cameraProviders, authenticationService, databasePaths);
        DataContext = _viewModel;
        Closed += (_, _) => _viewModel.Dispose();
    }
}
