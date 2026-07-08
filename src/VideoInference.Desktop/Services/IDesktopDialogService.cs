namespace VideoInferenceDemo;

public interface IDesktopDialogService
{
    string? OpenVideoFile();

    bool ShowFsmConfig(FsmConfigViewModel viewModel);

    bool ShowSopConfig(SopConfigViewModel viewModel);

    bool ShowCameraConfig(CameraConfigViewModel viewModel);

    bool ShowSystemSettings(SystemSettingsViewModel viewModel);

    void ShowActionLabel(ActionLabelViewModel viewModel);

    string? SaveCsvFilePath(string defaultFileName, string title);

    void ShowPerformanceDiagnostics(PerformanceDiagnosticsViewModel viewModel);

    void ShowPersonnelManagement(PersonnelManagementViewModel viewModel);

    bool ShowLogin(PersonnelAuthenticationService authenticationService);

    bool ConfirmAdminPassword(PersonnelRepository personnelRepository);

    string? RequestPersonnelPassword(PersonnelEditorItem item);

    void ShowProductionDashboard(ProductionDashboardViewModel viewModel);
}
