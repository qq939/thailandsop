namespace VideoInferenceDemo;

internal sealed class PipelineTaskRuntimeState : IDisposable
{
    private readonly object _sync = new();
    private IVisionTask? _primaryTask;
    private List<IVisionTask> _sidecarTasks = new();
    private string? _modelVersion;

    public void UpdateClassNames(string[]? classNames)
    {
        lock (_sync)
        {
            _primaryTask?.UpdateClassNames(classNames);
        }
    }

    public void WarmupPrimary(int width, int height, Action<string?> emitDeviceChanged)
    {
        ArgumentNullException.ThrowIfNull(emitDeviceChanged);

        IVisionTask? target;
        lock (_sync)
        {
            target = _primaryTask;
        }

        if (target == null)
        {
            return;
        }

        try
        {
            target.Warmup(width, height);
            emitDeviceChanged(target.ActiveDeviceLabel);
        }
        catch
        {
            // Ignore warmup failures; runtime will report errors on actual inference.
        }
    }

    public VisionWorkerStatusSnapshot? GetPrimaryWorkerStatus()
    {
        IVisionTask? target;
        lock (_sync)
        {
            target = _primaryTask;
        }

        return target is IWorkerStatusProvider provider
            ? provider.TryGetWorkerStatus()
            : null;
    }

    public PipelineTaskRuntimeSnapshot? GetExecutionSnapshot()
    {
        lock (_sync)
        {
            if (_primaryTask == null)
            {
                return null;
            }

            return new PipelineTaskRuntimeSnapshot(
                _primaryTask,
                _sidecarTasks.ToArray(),
                _modelVersion);
        }
    }

    public void SetPrimaryTask(IVisionTask task, string? modelVersion = null, bool clearSidecars = false)
    {
        ArgumentNullException.ThrowIfNull(task);

        IVisionTask? oldPrimary;
        List<IVisionTask>? oldSidecars = null;
        lock (_sync)
        {
            oldPrimary = _primaryTask;
            _primaryTask = task;
            if (clearSidecars)
            {
                oldSidecars = _sidecarTasks;
                _sidecarTasks = new List<IVisionTask>();
            }

            _modelVersion = modelVersion;
        }

        oldPrimary?.Dispose();
        DisposeTasks(oldSidecars);
    }

    public void SetSidecarTasks(IEnumerable<IVisionTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        var newTasks = tasks.ToList();
        List<IVisionTask> oldTasks;
        lock (_sync)
        {
            oldTasks = _sidecarTasks;
            _sidecarTasks = newTasks;
        }

        DisposeTasks(oldTasks);
    }

    public void ClearSidecarTasks()
    {
        List<IVisionTask> oldTasks;
        lock (_sync)
        {
            oldTasks = _sidecarTasks;
            _sidecarTasks = new List<IVisionTask>();
        }

        DisposeTasks(oldTasks);
    }

    public void ClearTasks()
    {
        IVisionTask? oldPrimary;
        List<IVisionTask> oldSidecars;
        lock (_sync)
        {
            oldPrimary = _primaryTask;
            oldSidecars = _sidecarTasks;
            _primaryTask = null;
            _sidecarTasks = new List<IVisionTask>();
            _modelVersion = null;
        }

        oldPrimary?.Dispose();
        DisposeTasks(oldSidecars);
    }

    public void Dispose()
    {
        ClearTasks();
    }

    private static void DisposeTasks(IEnumerable<IVisionTask>? tasks)
    {
        if (tasks == null)
        {
            return;
        }

        foreach (var task in tasks)
        {
            task.Dispose();
        }
    }
}

internal sealed record PipelineTaskRuntimeSnapshot(
    IVisionTask PrimaryTask,
    IReadOnlyList<IVisionTask> SidecarTasks,
    string? ModelVersion);
