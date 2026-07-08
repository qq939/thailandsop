namespace VideoInferenceDemo;

public sealed class ObbDetectionPayload : VisionTaskPayload
{
    public ObbDetectionPayload(IReadOnlyList<YoloObbDetection> detections)
    {
        Detections = detections ?? Array.Empty<YoloObbDetection>();
    }

    public IReadOnlyList<YoloObbDetection> Detections { get; }
}
