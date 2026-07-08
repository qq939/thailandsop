namespace VideoInferenceDemo;

public readonly record struct PipelineSessionStartInfo(
    string SourceLabel,
    string SourceKey,
    string RunUuid,
    long RunStartedUtcMs);

public enum PipelineCameraStartFailureKind
{
    Disabled,
    Blocked,
    NoModel,
    StartFailed
}

public sealed record PipelineCameraStartResult(
    bool Success,
    PipelineSessionStartInfo? StartInfo = null,
    string? Message = null,
    PipelineCameraStartFailureKind? FailureKind = null,
    Exception? Exception = null)
{
    public SessionRunState RunState => Success
        ? SessionRunState.Running
        : FailureKind switch
        {
            PipelineCameraStartFailureKind.Disabled => SessionRunState.Disabled,
            PipelineCameraStartFailureKind.NoModel => SessionRunState.NoModel,
            PipelineCameraStartFailureKind.Blocked => SessionRunState.Blocked,
            _ => SessionRunState.Error
        };

    public SessionStatusSnapshot ToStatusSnapshot()
    {
        return new SessionStatusSnapshot(
            RunState,
            false,
            SessionTransitionState.Unknown,
            Message);
    }
}

public sealed class PipelineSessionController
{
    private readonly VideoPipeline _pipeline;
    private readonly SqliteResultWriter _resultWriter;
    private string _currentSourceKey = string.Empty;
    private string _currentRunUuid = string.Empty;
    private long _currentRunStartedUtcMs;
    private string _activePrimaryTaskId = string.Empty;
    private IReadOnlyList<string> _activeSidecarTaskIds = Array.Empty<string>();

    public PipelineSessionController(VideoPipeline pipeline, SqliteResultWriter resultWriter)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _resultWriter = resultWriter ?? throw new ArgumentNullException(nameof(resultWriter));
    }

    public string CurrentSourceKey => _currentSourceKey;
    public string CurrentRunUuid => _currentRunUuid;
    public long CurrentRunStartedUtcMs => _currentRunStartedUtcMs;
    public bool IsPrimaryTaskLoaded => !string.IsNullOrWhiteSpace(_activePrimaryTaskId);
    public string ActivePrimaryTaskId => _activePrimaryTaskId;
    public IReadOnlyList<string> ActiveSidecarTaskIds => _activeSidecarTaskIds;
    public VisionWorkerStatusSnapshot? GetPrimaryWorkerStatus() => _pipeline.GetPrimaryWorkerStatus();

    public void ApplyPrimaryTask(
        VisionTaskDefinition definition,
        VisionTaskFactoryRegistry registry,
        VisionTaskCreationContext context)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(registry);

        var task = registry.Create(definition, context);
        _pipeline.SetPrimaryTask(task, ResolveTaskVersion(definition), clearSidecars: true);
        _activePrimaryTaskId = definition.Id;
        _activeSidecarTaskIds = Array.Empty<string>();
    }

    public void ApplySidecarTasks(
        IEnumerable<VisionTaskDefinition> definitions,
        VisionTaskFactoryRegistry registry,
        VisionTaskCreationContext context)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(registry);

        var items = definitions.ToArray();
        var tasks = items.Select(definition => registry.Create(definition, context)).ToArray();
        _pipeline.SetSidecarTasks(tasks);
        _activeSidecarTaskIds = items.Select(item => item.Id).ToArray();
    }

    public void ClearSidecarTasks()
    {
        _pipeline.ClearSidecarTasks();
        _activeSidecarTaskIds = Array.Empty<string>();
    }

    public void SetPrimaryTask(IVisionTask task, VisionTaskDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(definition);

        _pipeline.SetPrimaryTask(task, ResolveTaskVersion(definition), clearSidecars: true);
        _activePrimaryTaskId = definition.Id;
        _activeSidecarTaskIds = Array.Empty<string>();
    }

    public PipelineSessionStartInfo StartVideo(string path, double targetFps, bool useSourcePtsForVideo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _currentSourceKey = path;
        _pipeline.StartVideo(path, targetFps, useSourcePtsForVideo);
        CaptureRunContext();
        return new PipelineSessionStartInfo(path, _currentSourceKey, _currentRunUuid, _currentRunStartedUtcMs);
    }

    public PipelineCameraStartResult TryStartCamera(CameraProfile profile, Func<SessionStartPrecheckResult>? prepareStart = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        CameraDiagnostics.Info(
            "session-controller",
            $"Camera start requested. Name={profile.Name}, Enabled={profile.Enabled}, PrimaryTaskLoaded={IsPrimaryTaskLoaded}");

        if (!profile.Enabled)
        {
            return new PipelineCameraStartResult(
                false,
                Message: $"Camera '{profile.Name}' is disabled.",
                FailureKind: PipelineCameraStartFailureKind.Disabled);
        }

        var precheck = prepareStart?.Invoke() ?? SessionStartPrecheckResult.Success;
        if (!precheck.IsSuccess)
        {
            CameraDiagnostics.Info(
                "session-controller",
                $"Camera start blocked. Name={profile.Name}, Reason={precheck.Message}");
            return new PipelineCameraStartResult(
                false,
                Message: precheck.Message,
                FailureKind: PipelineCameraStartFailureKind.Blocked);
        }

        if (!IsPrimaryTaskLoaded)
        {
            CameraDiagnostics.Info(
                "session-controller",
                $"Camera start blocked. Name={profile.Name}, Reason=No primary task loaded.");
            return new PipelineCameraStartResult(
                false,
                Message: "No primary task loaded for this camera session.",
                FailureKind: PipelineCameraStartFailureKind.NoModel);
        }

        var options = profile.BuildOpenOptions(profile.TargetFps);
        _currentSourceKey = BuildCameraSourceKey(options);
        try
        {
            CameraDiagnostics.Info(
                "session-controller",
                $"Starting camera session. Name={profile.Name}, Provider={options.ProviderId}, Selector={CameraOptionHelpers.GetSelector(options)}, TargetFps={profile.TargetFps:F2}");
            _pipeline.StartCamera(options, profile.TargetFps, profile.BuildRecordingOptions());
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Error("session-controller", $"Failed to start camera session '{profile.Name}'.", ex);
            return new PipelineCameraStartResult(
                false,
                Message: ex.Message,
                FailureKind: PipelineCameraStartFailureKind.StartFailed,
                Exception: ex);
        }

        CaptureRunContext();
        var startInfo = new PipelineSessionStartInfo(
            BuildSourceLabel(options),
            _currentSourceKey,
            _currentRunUuid,
            _currentRunStartedUtcMs);
        CameraDiagnostics.Info(
            "session-controller",
            $"Camera start succeeded. Name={profile.Name}, RunUuid={_currentRunUuid}, SourceKey={_currentSourceKey}");
        return new PipelineCameraStartResult(true, startInfo);
    }

    public void Stop(TcnPredictionRecorder? predictionRecorder, string status)
    {
        predictionRecorder?.Flush();
        _pipeline.Stop();
        FinalizeCurrentRun(status);
        _currentSourceKey = string.Empty;
    }

    public void RequestRecordingRotate(string? reason = null)
    {
        _pipeline.RequestRecordingRotate(reason);
    }

    public void ResetSourceKey()
    {
        _currentSourceKey = string.Empty;
    }

    public void ClearTasks()
    {
        _pipeline.ClearTasks();
        _activePrimaryTaskId = string.Empty;
        _activeSidecarTaskIds = Array.Empty<string>();
    }

    private void CaptureRunContext()
    {
        _currentRunUuid = _pipeline.CurrentRunUuid ?? string.Empty;
        _currentRunStartedUtcMs = _pipeline.CurrentRunStartedUtcMs;
    }

    private void FinalizeCurrentRun(string status)
    {
        if (!string.IsNullOrWhiteSpace(_currentRunUuid))
        {
            _resultWriter.MarkRunEnded(_currentRunUuid, status);
        }

        _currentRunUuid = string.Empty;
        _currentRunStartedUtcMs = 0;
    }

    private static string BuildSourceLabel(CameraOpenOptions options)
    {
        return $"{options.ProviderId} {CameraOptionHelpers.GetSelector(options)}";
    }

    private static string BuildCameraSourceKey(CameraOpenOptions options)
    {
        return $"camera:{options.ProviderId}:{CameraOptionHelpers.GetSelector(options)}";
    }

    private static string ResolveTaskVersion(VisionTaskDefinition definition)
    {
        if (definition.Metadata.TryGetValue("modelPath", out var modelPath) &&
            !string.IsNullOrWhiteSpace(modelPath))
        {
            return modelPath;
        }

        return definition.BundlePath ?? definition.Id;
    }
}
