using System.IO;
using System.Linq;

namespace VideoInferenceDemo;

public sealed class ModelWorkspaceService
{
    private readonly string _baseDirectory;
    private readonly string _selectionPath;
    private IReadOnlyList<ModelCatalogEntry> _availableModels = Array.Empty<ModelCatalogEntry>();
    private ModelCatalogEntry? _preferredModelSource;
    private ModelCatalogEntry? _activatedModelSource;

    public ModelWorkspaceService(string baseDirectory, string? selectionPath = null)
    {
        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : baseDirectory;
        _selectionPath = string.IsNullOrWhiteSpace(selectionPath)
            ? Path.Combine(_baseDirectory, "model_selection.json")
            : selectionPath;
    }

    public IReadOnlyList<ModelCatalogEntry> AvailableModels => _availableModels;
    public ModelCatalogEntry? PreferredModelSource => _preferredModelSource;
    public ModelCatalogEntry? ActivatedModelSource => _activatedModelSource;

    public ModelWorkspaceSnapshot ReloadCatalog(string? preferredModelSourceId = null)
    {
        preferredModelSourceId = string.IsNullOrWhiteSpace(preferredModelSourceId)
            ? _preferredModelSource?.Id
            : preferredModelSourceId;
        preferredModelSourceId ??= ModelSelectionStorage.LoadPreferredModelSourceId(_selectionPath);

        _availableModels = DiscoverAvailableModels(_baseDirectory);
        _preferredModelSource = FindById(preferredModelSourceId) ?? _availableModels.FirstOrDefault();

        if (_activatedModelSource != null && FindById(_activatedModelSource.Id) == null)
        {
            _activatedModelSource = null;
        }

        PersistPreferredModelSource();
        return Snapshot();
    }

    public ModelWorkspaceSnapshot MarkActivatedModelSource(ModelCatalogEntry? model)
    {
        _activatedModelSource = FindById(model?.Id);
        if (_activatedModelSource != null)
        {
            _preferredModelSource = _activatedModelSource;
            PersistPreferredModelSource();
        }

        return Snapshot();
    }

    public ModelWorkspaceSnapshot ClearActivatedModelSource()
    {
        _activatedModelSource = null;
        return Snapshot();
    }

    public ModelWorkspaceSnapshot Snapshot()
    {
        return new ModelWorkspaceSnapshot(
            _availableModels,
            _preferredModelSource,
            _activatedModelSource);
    }

    public static IReadOnlyList<ModelCatalogEntry> DiscoverAvailableModels(string baseDirectory)
    {
        var dlRoot = Path.Combine(baseDirectory, "DL");
        var models = ModelCatalog.Discover(dlRoot);
        if (models.Count > 0)
        {
            return models;
        }

        return ModelCatalog.Discover(baseDirectory);
    }

    private ModelCatalogEntry? FindById(string? id)
    {
        return string.IsNullOrWhiteSpace(id)
            ? null
            : _availableModels.FirstOrDefault(model =>
                string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private void PersistPreferredModelSource()
    {
        ModelSelectionStorage.SavePreferredModelSourceId(_selectionPath, _preferredModelSource?.Id);
    }
}
