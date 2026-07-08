namespace VideoInferenceDemo;

public sealed class MediaPipeHandTaskMetadata
{
    public string TaskFilePath { get; init; } = string.Empty;
    public string WorkerKind { get; init; } = "mediapipe_hand";
    public string WorkerPythonPath { get; init; } = "python";
    public string WorkerScriptPath { get; init; } = string.Empty;
    public VisionWorkerProtocolKind WorkerProtocol { get; init; } = VisionWorkerProtocolKind.NamedPipe;
    public int MaxHands { get; init; } = 2;
    public float MinHandDetectionConfidence { get; init; } = 0.5f;
    public float MinHandPresenceConfidence { get; init; } = 0.5f;
    public float MinTrackingConfidence { get; init; } = 0.5f;
    public int PreferredInputSize { get; init; } = 640;
}
