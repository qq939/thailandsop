namespace VideoInferenceDemo;

public sealed class DetectionPayload : VisionTaskPayload
{
    public DetectionPayload(IReadOnlyList<DetectionEntity> detections)
    {
        Detections = detections ?? Array.Empty<DetectionEntity>();
    }

    public IReadOnlyList<DetectionEntity> Detections { get; }
}
