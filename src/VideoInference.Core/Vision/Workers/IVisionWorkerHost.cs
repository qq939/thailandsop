namespace VideoInferenceDemo;

public interface IVisionWorkerHost : IDisposable
{
    string WorkerKind { get; }
    string EndpointName { get; }
    string? ActiveRuntimeLabel { get; }
    VisionWorkerState State { get; }
    VisionWorkerStatusSnapshot GetStatusSnapshot();
    void EnsureStarted();
    void Stop();
    IVisionWorkerClient CreateClient();
}
