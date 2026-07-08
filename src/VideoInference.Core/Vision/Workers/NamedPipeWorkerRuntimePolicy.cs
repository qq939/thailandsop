namespace VideoInferenceDemo;

internal sealed class NamedPipeWorkerRuntimePolicy
{
    private readonly string _endpointName;
    private readonly NamedPipeWorkerRuntimeStateTracker _stateTracker;

    public NamedPipeWorkerRuntimePolicy(
        string endpointName,
        NamedPipeWorkerRuntimeStateTracker stateTracker)
    {
        _endpointName = string.IsNullOrWhiteSpace(endpointName)
            ? throw new ArgumentException("Endpoint name is required.", nameof(endpointName))
            : endpointName;
        _stateTracker = stateTracker ?? throw new ArgumentNullException(nameof(stateTracker));
    }

    public int? LastExitCode => _stateTracker.LastExitCode;

    public void OnExistingProcessObserved()
    {
        _stateTracker.MarkStartingForExistingProcess();
    }

    public void OnStartInitiated()
    {
        _stateTracker.ResetForStart();
    }

    public void OnStartCompleted(string? runtimeLabel)
    {
        _stateTracker.MarkReady(runtimeLabel);
    }

    public void OnStartFailed(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        _stateTracker.MarkFaulted(ex.Message);
    }

    public VisionWorkerState OnRequestStarted()
    {
        return _stateTracker.MarkBusy();
    }

    public void OnRequestCompleted(VisionWorkerResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        _stateTracker.CompleteRequest(response.IsSuccess, response.RuntimeLabel);
    }

    public void OnRequestFailed(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        _stateTracker.MarkRequestFault(ex.Message);
    }

    public void OnRequestFinished(VisionWorkerState previousState)
    {
        _stateTracker.RestoreAfterBusy(previousState);
    }

    public void OnPingCompleted(bool isAlive)
    {
        if (isAlive)
        {
            _stateTracker.MarkReady();
        }
        else
        {
            _stateTracker.MarkDegraded();
        }
    }

    public void OnPingFailed()
    {
        _stateTracker.MarkDegraded();
    }

    public void OnStopped()
    {
        _stateTracker.MarkStopped();
    }

    public void OnUnexpectedExit(int? exitCode)
    {
        _stateTracker.RecordProcessExit(
            exitCode,
            $"Worker process exited unexpectedly for endpoint '{_endpointName}'. ExitCode={exitCode?.ToString() ?? "unknown"}.");
    }
}
