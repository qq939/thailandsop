namespace VideoInferenceDemo.ImageInspection;

public sealed class InspectionWorkspaceSidePanelViewModel
{
    private InspectionWorkspaceSidePanelViewModel(
        InspectionWorkspaceKind kind,
        string title,
        string subtitle,
        InspectionTaskSessionViewModel? task,
        IReadOnlyList<InspectionTaskSessionViewModel> tasks,
        IReadOnlyList<InspectionCameraSessionViewModel> cameras)
    {
        Kind = kind;
        Title = title;
        Subtitle = subtitle;
        Task = task;
        Tasks = tasks;
        Cameras = cameras;
    }

    public InspectionWorkspaceKind Kind { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public InspectionTaskSessionViewModel? Task { get; }

    public IReadOnlyList<InspectionTaskSessionViewModel> Tasks { get; }

    public IReadOnlyList<InspectionCameraSessionViewModel> Cameras { get; }

    public bool IsOverview => Kind == InspectionWorkspaceKind.Overview;

    public bool IsTask => Kind == InspectionWorkspaceKind.Task || Kind == InspectionWorkspaceKind.Camera;

    public static InspectionWorkspaceSidePanelViewModel CreateOverview(
        IReadOnlyList<InspectionTaskSessionViewModel> tasks,
        IReadOnlyList<InspectionCameraSessionViewModel> cameras)
    {
        return new InspectionWorkspaceSidePanelViewModel(
            InspectionWorkspaceKind.Overview,
            "总览",
            $"{tasks.Count} 个任务 / {cameras.Count} 个相机",
            null,
            tasks,
            cameras);
    }

    public static InspectionWorkspaceSidePanelViewModel CreateTask(InspectionTaskSessionViewModel? task)
    {
        if (task == null)
        {
            return new InspectionWorkspaceSidePanelViewModel(
                InspectionWorkspaceKind.Task,
                "未加载任务",
                "没有可用任务",
                null,
                [],
                []);
        }

        return new InspectionWorkspaceSidePanelViewModel(
            InspectionWorkspaceKind.Task,
            task.Name,
            $"{task.StationId} / {task.ActionType}",
            task,
            [task],
            task.Cameras);
    }
}
