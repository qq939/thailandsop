using System.IO;

namespace VideoInferenceDemo;

public sealed class VisionWorkspaceService
{
    private readonly string _baseDirectory;
    private readonly ModelWorkspaceService _modelWorkspaceService;
    private readonly ModelActivationService _modelActivationService;
    private IReadOnlyList<VisionTaskDefinition> _availableVisionTasks = Array.Empty<VisionTaskDefinition>();
    private VisionTaskDefinition? _selectedPrimaryTask;
    private string? _selectedModelSourceId;
    private VisionWorkspaceSnapshot _currentSnapshot;

    public VisionWorkspaceService(
        string baseDirectory,
        ModelActivationService? modelActivationService = null,
        ModelWorkspaceService? modelWorkspaceService = null)
    {
        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : baseDirectory;
        _modelActivationService = modelActivationService ?? new ModelActivationService();
        _modelWorkspaceService = modelWorkspaceService ?? new ModelWorkspaceService(_baseDirectory);
        _currentSnapshot = CreateSnapshot();
    }

    public VisionWorkspaceSnapshot CurrentSnapshot => _currentSnapshot;

    public VisionWorkspaceSnapshot ReloadCatalog(string? preferredModelId = null, string? preferredTaskId = null)
    {
        var modelSnapshot = _modelWorkspaceService.ReloadCatalog(preferredModelId ?? _selectedModelSourceId);
        _availableVisionTasks = VisionTaskCatalog.Discover(_baseDirectory);
        _selectedPrimaryTask = ResolveSelectedPrimaryTask(preferredTaskId, preferredModelId ?? modelSnapshot.PreferredModelSource?.Id);
        _selectedModelSourceId = ResolveModelSourceId(preferredModelId, modelSnapshot.PreferredModelSource?.Id, _selectedPrimaryTask);
        return UpdateSnapshot();
    }

    public VisionWorkspaceSnapshot SelectPrimaryTask(VisionTaskDefinition? task)
    {
        _selectedPrimaryTask = ResolveTaskById(task?.Id)
            ?? (task == null ? null : ResolveTaskByRuntime(task));
        _selectedModelSourceId = ResolveModelForTask(_selectedPrimaryTask)?.Id;
        return UpdateSnapshot();
    }

    public VisionWorkspaceSnapshot SelectVisionTask(VisionTaskDefinition? task)
    {
        return SelectPrimaryTask(task);
    }

    public VisionWorkspaceSnapshot MarkActivatedModelSource(ModelCatalogEntry? model)
    {
        _modelWorkspaceService.MarkActivatedModelSource(model);
        return UpdateSnapshot();
    }

    public VisionWorkspaceSnapshot ClearActivatedModelSource()
    {
        _modelWorkspaceService.ClearActivatedModelSource();
        return UpdateSnapshot();
    }

    public VisionWorkspaceActivationResult TryMaterializePrimaryTaskModelSourceBinding(
        VisionTaskCreationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var selectedModelSource = ResolveSelectedModelSource();
        var attempt = _modelActivationService.TryCreateModelBinding(
            selectedModelSource,
            context.OnnxDeviceKind,
            context.ConfidenceThreshold,
            context.NmsThreshold);
        if (attempt.Success)
        {
            _modelWorkspaceService.MarkActivatedModelSource(selectedModelSource);
        }
        else
        {
            _modelWorkspaceService.ClearActivatedModelSource();
        }

        return new VisionWorkspaceActivationResult(UpdateSnapshot(), attempt);
    }

    private VisionWorkspaceSnapshot UpdateSnapshot()
    {
        _currentSnapshot = CreateSnapshot();
        return _currentSnapshot;
    }

    private VisionWorkspaceSnapshot CreateSnapshot()
    {
        var modelSnapshot = _modelWorkspaceService.Snapshot();
        var primaryTaskModelSource = ResolvePrimaryTaskModelSource();
        return new VisionWorkspaceSnapshot(
            modelSnapshot,
            _availableVisionTasks,
            _selectedPrimaryTask,
            ResolvePrimarySelectionKind(_selectedPrimaryTask, primaryTaskModelSource),
            primaryTaskModelSource,
            ModelWorkspaceStatusInfo.FromSnapshot(modelSnapshot),
            IsPrimaryTaskModelActive(primaryTaskModelSource));
    }

    private VisionTaskDefinition? ResolveSelectedPrimaryTask(string? preferredTaskId, string? selectedModelId)
    {
        return ResolveTaskById(preferredTaskId)
            ?? ResolveTaskById(selectedModelId)
            ?? _selectedPrimaryTask
            ?? _availableVisionTasks.FirstOrDefault();
    }

    private static VisionWorkspacePrimarySelectionKind ResolvePrimarySelectionKind(
        VisionTaskDefinition? selectedPrimaryTask,
        ModelCatalogEntry? primaryTaskModelSource)
    {
        if (selectedPrimaryTask == null)
        {
            return VisionWorkspacePrimarySelectionKind.None;
        }

        return primaryTaskModelSource != null
            ? VisionWorkspacePrimarySelectionKind.ModelCatalog
            : VisionWorkspacePrimarySelectionKind.TaskCatalog;
    }

    private VisionTaskDefinition? ResolveTaskForModel(ModelCatalogEntry? model)
    {
        return ResolveTaskById(model?.Id);
    }

    private VisionTaskDefinition? ResolveTaskById(string? taskId)
    {
        return string.IsNullOrWhiteSpace(taskId)
            ? null
            : _availableVisionTasks.FirstOrDefault(task =>
                string.Equals(task.Id, taskId, StringComparison.OrdinalIgnoreCase));
    }

    private VisionTaskDefinition? ResolveTaskByRuntime(VisionTaskDefinition? task)
    {
        if (task == null)
        {
            return null;
        }

        return _availableVisionTasks.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, task.Id, StringComparison.OrdinalIgnoreCase) ||
            (candidate.TaskKind == task.TaskKind && candidate.RuntimeKind == task.RuntimeKind));
    }

    private ModelCatalogEntry? ResolveModelForTask(VisionTaskDefinition? task)
    {
        return task == null
            ? null
            : _modelWorkspaceService.AvailableModels.FirstOrDefault(model =>
                string.Equals(model.Id, task.Id, StringComparison.OrdinalIgnoreCase));
    }

    private ModelCatalogEntry? ResolveSelectedModelSource()
    {
        return ResolveModelById(_selectedModelSourceId)
            ?? ResolveModelForTask(_selectedPrimaryTask);
    }

    private ModelCatalogEntry? ResolvePrimaryTaskModelSource()
    {
        var matchingModel = ResolveModelForTask(_selectedPrimaryTask);
        if (matchingModel != null)
        {
            _selectedModelSourceId = matchingModel.Id;
            return matchingModel;
        }

        return null;
    }

    private ModelCatalogEntry? ResolveModelById(string? modelId)
    {
        return string.IsNullOrWhiteSpace(modelId)
            ? null
            : _modelWorkspaceService.AvailableModels.FirstOrDefault(model =>
                string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
    }

    private string? ResolveModelSourceId(string? preferredModelId, string? snapshotSelectedModelId, VisionTaskDefinition? selectedPrimaryTask)
    {
        return ResolveModelById(preferredModelId)?.Id
            ?? ResolveModelById(snapshotSelectedModelId)?.Id
            ?? ResolveModelForTask(selectedPrimaryTask)?.Id;
    }

    private bool IsPrimaryTaskModelActive(ModelCatalogEntry? primaryTaskModelSource)
    {
        return primaryTaskModelSource != null &&
               _modelWorkspaceService.ActivatedModelSource != null &&
               string.Equals(primaryTaskModelSource.Id, _modelWorkspaceService.ActivatedModelSource.Id, StringComparison.OrdinalIgnoreCase) &&
               File.Exists(primaryTaskModelSource.ModelPath);
    }
}
