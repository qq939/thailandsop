namespace VideoInferenceDemo;

public sealed class SegmentationPayload : VisionTaskPayload
{
    public SegmentationPayload(IReadOnlyList<YoloSegmentation> segmentations, IReadOnlyList<DetectionEntity> detections)
    {
        Segmentations = segmentations ?? Array.Empty<YoloSegmentation>();
        Detections = detections ?? Array.Empty<DetectionEntity>();
    }

    public IReadOnlyList<YoloSegmentation> Segmentations { get; }
    public IReadOnlyList<DetectionEntity> Detections { get; }
}
