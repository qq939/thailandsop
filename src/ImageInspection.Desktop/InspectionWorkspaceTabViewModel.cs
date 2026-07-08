namespace VideoInferenceDemo.ImageInspection;

public sealed class InspectionWorkspaceTabViewModel
{
    private InspectionWorkspaceTabViewModel(
        InspectionWorkspaceKind kind,
        string title,
        InspectionTaskSessionViewModel? task,
        InspectionCameraSessionViewModel? camera)
    {
        Kind = kind;
        Title = title;
        Task = task;
        Camera = camera;
    }

    public InspectionWorkspaceKind Kind { get; }

    public string Title { get; }

    public InspectionTaskSessionViewModel? Task { get; }

    public InspectionCameraSessionViewModel? Camera { get; }

    public bool IsOverview => Kind == InspectionWorkspaceKind.Overview;

    public bool IsTask => Kind == InspectionWorkspaceKind.Task;

    public bool IsCamera => Kind == InspectionWorkspaceKind.Camera;

    public static InspectionWorkspaceTabViewModel CreateOverview()
    {
        return new InspectionWorkspaceTabViewModel(InspectionWorkspaceKind.Overview, "总览", null, null);
    }

    public static InspectionWorkspaceTabViewModel CreateTask(InspectionTaskSessionViewModel task)
    {
        return new InspectionWorkspaceTabViewModel(InspectionWorkspaceKind.Task, task.Name, task, null);
    }

    public static InspectionWorkspaceTabViewModel CreateCamera(InspectionCameraSessionViewModel camera)
    {
        return new InspectionWorkspaceTabViewModel(InspectionWorkspaceKind.Camera, camera.Name, null, camera);
    }
}
