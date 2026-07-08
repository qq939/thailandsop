namespace VideoInferenceDemo;

public interface IVisionWorkerClient : IDisposable
{
    string WorkerKind { get; }
    string EndpointName { get; }
    string? ActiveRuntimeLabel { get; }
    VisionWorkerState State { get; }
    VisionWorkerStatusSnapshot Status { get; }
    VisionWorkerResponse Execute(VisionWorkerRequest request, TimeSpan timeout);
    bool TryPing(TimeSpan timeout);
}
