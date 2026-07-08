namespace VideoInferenceDemo;

public interface IWorkerStatusProvider
{
    VisionWorkerStatusSnapshot? TryGetWorkerStatus();
}
