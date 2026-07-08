namespace VideoInferenceDemo;

internal sealed class NamedPipeWorkerRuntimeStateTracker
{
    private readonly string _workerKind;
    private readonly string _endpointName;
    private readonly NamedPipeWorkerDiagnosticsState _diagnostics;
    private VisionWorkerState _state = VisionWorkerState.Created;

    public NamedPipeWorkerRuntimeStateTracker(
        string workerKind,
        string endpointName,
        VisionWorkerProtocolKind protocolKind)
    {
        _workerKind = string.IsNullOrWhiteSpace(workerKind)
            ? throw new ArgumentException("Worker kind is required.", nameof(workerKind))
            : workerKind;
        _endpointName = string.IsNullOrWhiteSpace(endpointName)
            ? throw new ArgumentException("Endpoint name is required.", nameof(endpointName))
            : endpointName;
        _diagnostics = new NamedPipeWorkerDiagnosticsState(protocolKind);
    }

    public string? ActiveRuntimeLabel => _diagnostics.BuildRuntimeLabel();
    public VisionWorkerState State => _state;
    public int? LastExitCode => _diagnostics.LastExitCode;

    public VisionWorkerStatusSnapshot CreateSnapshot(int? processId)
    {
        return _diagnostics.CreateSnapshot(
            _workerKind,
            _endpointName,
            _state,
            processId);
    }

    public void MarkStartingForExistingProcess()
    {
        if (_state == VisionWorkerState.Created || _state == VisionWorkerState.Stopped)
        {
            _state = VisionWorkerState.Starting;
        }
    }

    public void ResetForStart()
    {
        _diagnostics.Reset();
        _state = VisionWorkerState.Starting;
    }

    public void MarkReady(string? runtimeLabel = null)
    {
        _diagnostics.RecordRuntimeLabel(runtimeLabel);
        _state = VisionWorkerState.Ready;
    }

    public VisionWorkerState MarkBusy()
    {
        var previousState = _state;
        _state = VisionWorkerState.Busy;
        return previousState;
    }

    public void CompleteRequest(bool isSuccess, string? runtimeLabel)
    {
        _diagnostics.RecordRuntimeLabel(runtimeLabel);
        _state = isSuccess ? VisionWorkerState.Ready : VisionWorkerState.Degraded;
    }

    public void RestoreAfterBusy(VisionWorkerState previousState)
    {
        if (_state == VisionWorkerState.Busy)
        {
            _state = previousState == VisionWorkerState.Created
                ? VisionWorkerState.Ready
                : previousState;
        }
    }

    public void MarkDegraded()
    {
        _state = VisionWorkerState.Degraded;
    }

    public void MarkStopped()
    {
        _state = VisionWorkerState.Stopped;
    }

    public void MarkFaulted(string message)
    {
        _diagnostics.RecordFault(message);
        _state = VisionWorkerState.Faulted;
    }

    public void MarkRequestFault(string message)
    {
        _diagnostics.RecordRequestFailure(message);
        _state = VisionWorkerState.Faulted;
    }

    public void RecordProcessExit(int? exitCode, string message)
    {
        _diagnostics.RecordExitCode(exitCode);
        _diagnostics.RecordFault(message);
        _state = VisionWorkerState.Faulted;
    }

    public void AppendStdErr(string? line)
    {
        _diagnostics.AppendStdErr(line);
    }
}
