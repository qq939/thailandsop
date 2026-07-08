namespace VideoInferenceDemo;

public enum ModelWorkspaceState
{
    Empty,
    Ready
}

public sealed record ModelWorkspaceStatusInfo(
    ModelWorkspaceState State,
    string Title,
    string Detail)
{
    public static ModelWorkspaceStatusInfo FromSnapshot(ModelWorkspaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.AvailableModels.Count == 0
            ? new ModelWorkspaceStatusInfo(
                ModelWorkspaceState.Empty,
                "无模型",
                "DL 目录下未发现模型目录。")
            : new ModelWorkspaceStatusInfo(
                ModelWorkspaceState.Ready,
                "模型可用",
                $"已发现 {snapshot.AvailableModels.Count} 个模型目录。");
    }
}
