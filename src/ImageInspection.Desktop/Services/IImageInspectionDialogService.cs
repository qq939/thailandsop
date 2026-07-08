namespace VideoInferenceDemo.ImageInspection.Services;

public interface IImageInspectionDialogService
{
    void ShowCameraSettings();

    void ShowTaskSettings();

    bool ShowRoiSettings(
        string productModel,
        string taskId,
        string positionNo,
        IReadOnlyList<InspectionCameraProfile> cameras);

    bool ShowParameterSettings();

    void ShowPersonnelManagement(PersonnelManagementViewModel viewModel);

    bool ShowLogin(PersonnelAuthenticationService authenticationService);

    bool ConfirmAdminPassword(PersonnelRepository personnelRepository);

    string? RequestPersonnelPassword(PersonnelEditorItem item);

    string? PickImageFile();
}
