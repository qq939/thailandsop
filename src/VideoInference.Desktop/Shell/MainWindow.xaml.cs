using System;
using System.IO;
using System.Windows;

namespace VideoInferenceDemo;

public partial class MainWindow : Window
{
    private readonly CameraProviderRegistry _cameraProviders = DesktopCameraProviderRegistry.CreateDefault();
    private readonly WpfDesktopDialogService _dialogService;
    private readonly WpfUiTimerFactory _uiTimerFactory;
    private readonly WpfUiDispatcher _uiDispatcher;
    private readonly DesktopPipelineSupportFactory _pipelineSupportFactory;
    private readonly DesktopEnvironmentDiagnosticsService _environmentDiagnosticsService;
    private readonly DesktopNativeRuntimeService _nativeRuntimeService;
    private readonly MainViewModel _viewModel;

    public MainWindow(PersonnelAuthenticationService authenticationService, WorkspaceDatabasePaths databasePaths)
    {
        InitializeComponent();

        var folderPickerService = new WpfFolderPickerService(this);
        _dialogService = new WpfDesktopDialogService(this, folderPickerService);
        _uiTimerFactory = new WpfUiTimerFactory(Dispatcher);
        _uiDispatcher = new WpfUiDispatcher(Dispatcher);
        _pipelineSupportFactory = new DesktopPipelineSupportFactory();
        _environmentDiagnosticsService = new DesktopEnvironmentDiagnosticsService();
        _nativeRuntimeService = new DesktopNativeRuntimeService();
        try
        {
            _viewModel = new MainViewModel(
                _cameraProviders,
                _dialogService,
                _uiTimerFactory,
                _uiDispatcher,
                _pipelineSupportFactory,
                _environmentDiagnosticsService,
                _nativeRuntimeService,
                authenticationService,
                databasePaths);
        }
        catch (Exception ex)
        {
            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "main_window_init_error.log"),
                ex.ToString());
            throw;
        }
        DataContext = _viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        _dialogService.Dispose();
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
