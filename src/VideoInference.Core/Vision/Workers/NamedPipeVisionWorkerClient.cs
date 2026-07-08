namespace VideoInferenceDemo;

internal sealed class NamedPipeVisionWorkerClient : IVisionWorkerClient
{
    private readonly NamedPipeWorkerRuntimeCoordinator _runtimeCoordinator;

    public NamedPipeVisionWorkerClient(NamedPipeWorkerRuntimeCoordinator runtimeCoordinator)
    {
        _runtimeCoordinator = runtimeCoordinator ?? throw new ArgumentNullException(nameof(runtimeCoordinator));
    }

    public string WorkerKind => _runtimeCoordinator.WorkerKind;
    public string EndpointName => _runtimeCoordinator.EndpointName;
    public string? ActiveRuntimeLabel => _runtimeCoordinator.ActiveRuntimeLabel;
    public VisionWorkerState State => _runtimeCoordinator.State;
    public VisionWorkerStatusSnapshot Status => _runtimeCoordinator.GetStatusSnapshot();

    public VisionWorkerResponse Execute(VisionWorkerRequest request, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(request);
        _runtimeCoordinator.EnsureStarted();
        return _runtimeCoordinator.ExecuteRequest(request, timeout);
    }

    public bool TryPing(TimeSpan timeout)
    {
        _runtimeCoordinator.EnsureStarted();
        return _runtimeCoordinator.PingWorker(timeout);
    }

    public void Dispose()
    {
    }
}
