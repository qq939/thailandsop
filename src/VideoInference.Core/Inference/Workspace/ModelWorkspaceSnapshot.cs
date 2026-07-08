namespace VideoInferenceDemo;

public sealed record ModelWorkspaceSnapshot(
    IReadOnlyList<ModelCatalogEntry> AvailableModels,
    ModelCatalogEntry? PreferredModelSource,
    ModelCatalogEntry? ActivatedModelSource);
