namespace VideoInferenceDemo;

public sealed class NamedPipeVisionWorkerHost : IVisionWorkerHost
{
    private readonly NamedPipeWorkerRuntimeCoordinator _runtimeCoordinator;

    public NamedPipeVisionWorkerHost(
        VisionWorkerHostOptions options,
        PythonProcessLauncher processLauncher)
    {
        _runtimeCoordinator = new NamedPipeWorkerRuntimeCoordinator(
            options ?? throw new ArgumentNullException(nameof(options)),
            processLauncher ?? throw new ArgumentNullException(nameof(processLauncher)));
    }

    public string WorkerKind => _runtimeCoordinator.WorkerKind;
    public string EndpointName => _runtimeCoordinator.EndpointName;
    public string? ActiveRuntimeLabel => _runtimeCoordinator.ActiveRuntimeLabel;
    public VisionWorkerState State => _runtimeCoordinator.State;

    public VisionWorkerStatusSnapshot GetStatusSnapshot() => _runtimeCoordinator.GetStatusSnapshot();

    public void EnsureStarted() => _runtimeCoordinator.EnsureStarted();

    public void Stop() => _runtimeCoordinator.Stop();

    public IVisionWorkerClient CreateClient()
    {
        return new NamedPipeVisionWorkerClient(_runtimeCoordinator);
    }

    public void Dispose() => _runtimeCoordinator.Dispose();
}
