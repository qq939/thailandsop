namespace VideoInferenceDemo;

public enum VisionWorkspacePrimarySelectionKind
{
    None,
    TaskCatalog,
    ModelCatalog
}

public sealed record VisionWorkspaceSnapshot(
    ModelWorkspaceSnapshot ModelWorkspace,
    IReadOnlyList<VisionTaskDefinition> AvailableVisionTasks,
    VisionTaskDefinition? SelectedPrimaryTask,
    VisionWorkspacePrimarySelectionKind PrimarySelectionKind,
    ModelCatalogEntry? PrimaryTaskModelSource,
    ModelWorkspaceStatusInfo WorkspaceStatusInfo,
    bool IsPrimaryTaskModelActive)
{
    public IReadOnlyList<ModelCatalogEntry> AvailableModels => ModelWorkspace.AvailableModels;
    public bool HasPrimaryTask => SelectedPrimaryTask != null;
    public bool IsModelBackedPrimaryTask => PrimarySelectionKind == VisionWorkspacePrimarySelectionKind.ModelCatalog;
    public ModelCatalogEntry? ActivatedModelSource => ModelWorkspace.ActivatedModelSource;
}
