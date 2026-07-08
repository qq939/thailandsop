namespace VideoInferenceDemo;

public enum VisionWorkerState
{
    Created,
    Starting,
    Ready,
    Busy,
    Degraded,
    Restarting,
    Stopped,
    Faulted
}
