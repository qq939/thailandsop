using Microsoft.Win32;
using System;
using System.Windows;

namespace VideoInferenceDemo;

public sealed class WpfDesktopDialogService : IDesktopDialogService, IDisposable
{
    private readonly Window _owner;
    private readonly IFolderPickerService _folderPickerService;
    private ActionLabelWindow? _labelWindow;
    private PerformanceDiagnosticsWindow? _performanceWindow;
    private PersonnelManagementWindow? _personnelWindow;
    private ProductionDashboardWindow? _dashboardWindow;

    public WpfDesktopDialogService(Window owner, IFolderPickerService folderPickerService)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _folderPickerService = folderPickerService ?? throw new ArgumentNullException(nameof(folderPickerService));
    }

    public string? OpenVideoFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Video Files|*.mp4;*.avi;*.mkv;*.mov|All Files|*.*",
            Title = "Open Video File"
        };

        return dialog.ShowDialog(_owner) == true ? dialog.FileName : null;
    }

    public bool ShowFsmConfig(FsmConfigViewModel viewModel)
    {
        var window = new FsmConfigWindow(viewModel)
        {
            Owner = _owner
        };

        return window.ShowDialog() == true;
    }

    public bool ShowSopConfig(SopConfigViewModel viewModel)
    {
        var window = new SopConfigWindow(viewModel)
        {
            Owner = _owner
        };

        return window.ShowDialog() == true;
    }

    public bool ShowCameraConfig(CameraConfigViewModel viewModel)
    {
        viewModel.FolderPickerService = _folderPickerService;

        var window = new CameraConfigWindow(viewModel)
        {
            Owner = _owner
        };

        return window.ShowDialog() == true;
    }

    public bool ShowSystemSettings(SystemSettingsViewModel viewModel)
    {
        var window = new SystemSettingsWindow(viewModel)
        {
            Owner = _owner
        };

        return window.ShowDialog() == true;
    }

    public void ShowActionLabel(ActionLabelViewModel viewModel)
    {
        if (_labelWindow == null)
        {
            _labelWindow = new ActionLabelWindow(viewModel)
            {
                Owner = _owner
            };
            _labelWindow.Closed += OnLabelWindowClosed;
            _labelWindow.Show();
            return;
        }

        if (_labelWindow.DataContext is ActionLabelViewModel oldViewModel)
        {
            oldViewModel.Dispose();
        }

        _labelWindow.DataContext = viewModel;
        if (!_labelWindow.IsVisible)
        {
            _labelWindow.Show();
        }

        _labelWindow.Activate();
    }

    public string? SaveCsvFilePath(string defaultFileName, string title)
    {
        var safeName = string.IsNullOrWhiteSpace(defaultFileName)
            ? $"production_dashboard_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            : defaultFileName.Trim();
        if (!safeName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            safeName += ".csv";
        }

        var dialog = new SaveFileDialog
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Export CSV" : title,
            Filter = "CSV Files|*.csv|All Files|*.*",
            FileName = safeName,
            AddExtension = true,
            OverwritePrompt = true
        };

        return dialog.ShowDialog(_owner) == true ? dialog.FileName : null;
    }

    public void ShowPerformanceDiagnostics(PerformanceDiagnosticsViewModel viewModel)
    {
        if (_performanceWindow == null)
        {
            _performanceWindow = new PerformanceDiagnosticsWindow(viewModel)
            {
                Owner = _owner
            };
            _performanceWindow.Closed += OnPerformanceWindowClosed;
            _performanceWindow.Show();
            return;
        }

        if (_performanceWindow.DataContext is PerformanceDiagnosticsViewModel oldViewModel)
        {
            oldViewModel.Dispose();
        }

        _performanceWindow.DataContext = viewModel;
        if (!_performanceWindow.IsVisible)
        {
            _performanceWindow.Show();
        }

        _performanceWindow.Activate();
    }

    public void ShowPersonnelManagement(PersonnelManagementViewModel viewModel)
    {
        if (_personnelWindow == null)
        {
            _personnelWindow = new PersonnelManagementWindow(viewModel)
            {
                Owner = _owner
            };
            _personnelWindow.Closed += OnPersonnelWindowClosed;
            _personnelWindow.Show();
            return;
        }

        if (_personnelWindow.DataContext is IDisposable oldViewModel)
        {
            oldViewModel.Dispose();
        }

        _personnelWindow.DataContext = viewModel;
        if (!_personnelWindow.IsVisible)
        {
            _personnelWindow.Show();
        }

        _personnelWindow.Activate();
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
        var owner = _personnelWindow?.IsVisible == true ? _personnelWindow : _owner;
        return PasswordChangeWindow.Request(owner, item);
    }

    public void ShowProductionDashboard(ProductionDashboardViewModel viewModel)
    {
        if (_dashboardWindow == null)
        {
            _dashboardWindow = new ProductionDashboardWindow(viewModel)
            {
                Owner = _owner
            };
            _dashboardWindow.Closed += OnDashboardWindowClosed;
            _dashboardWindow.Show();
            return;
        }

        _dashboardWindow.DataContext = viewModel;
        if (!_dashboardWindow.IsVisible)
        {
            _dashboardWindow.Show();
        }

        _dashboardWindow.Activate();
    }

    public void Dispose()
    {
        if (_labelWindow != null)
        {
            _labelWindow.Closed -= OnLabelWindowClosed;
            if (_labelWindow.DataContext is ActionLabelViewModel viewModel)
            {
                viewModel.Dispose();
            }

            _labelWindow.Close();
            _labelWindow = null;
        }

        if (_performanceWindow != null)
        {
            _performanceWindow.Closed -= OnPerformanceWindowClosed;
            if (_performanceWindow.DataContext is PerformanceDiagnosticsViewModel performanceViewModel)
            {
                performanceViewModel.Dispose();
            }

            _performanceWindow.Close();
            _performanceWindow = null;
        }

        if (_personnelWindow != null)
        {
            _personnelWindow.Closed -= OnPersonnelWindowClosed;
            _personnelWindow.Close();
            _personnelWindow = null;
        }

        if (_dashboardWindow != null)
        {
            _dashboardWindow.Closed -= OnDashboardWindowClosed;
            _dashboardWindow.Close();
            _dashboardWindow = null;
        }
    }

    private void OnLabelWindowClosed(object? sender, EventArgs e)
    {
        if (_labelWindow?.DataContext is ActionLabelViewModel viewModel)
        {
            viewModel.Dispose();
        }

        if (_labelWindow != null)
        {
            _labelWindow.Closed -= OnLabelWindowClosed;
        }

        _labelWindow = null;
    }

    private void OnPerformanceWindowClosed(object? sender, EventArgs e)
    {
        if (_performanceWindow?.DataContext is PerformanceDiagnosticsViewModel viewModel)
        {
            viewModel.Dispose();
        }

        if (_performanceWindow != null)
        {
            _performanceWindow.Closed -= OnPerformanceWindowClosed;
        }

        _performanceWindow = null;
    }

    private void OnPersonnelWindowClosed(object? sender, EventArgs e)
    {
        if (_personnelWindow != null)
        {
            _personnelWindow.Closed -= OnPersonnelWindowClosed;
        }

        _personnelWindow = null;
    }

    private void OnDashboardWindowClosed(object? sender, EventArgs e)
    {
        if (_dashboardWindow != null)
        {
            _dashboardWindow.Closed -= OnDashboardWindowClosed;
        }

        _dashboardWindow = null;
    }
}
