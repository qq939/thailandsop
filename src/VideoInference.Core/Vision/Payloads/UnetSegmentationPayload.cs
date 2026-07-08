namespace VideoInferenceDemo;

public sealed class UnetSegmentationPayload : VisionTaskPayload
{
    public UnetSegmentationPayload(UnetSegmentationResult result, IReadOnlyList<DetectionEntity> detections)
    {
        Result = result ?? new UnetSegmentationResult(Array.Empty<UnetDefectComponent>(), 0, Array.Empty<byte>(), 0, 0, UnetSegmentationMetadata.Default);
        Detections = detections ?? Array.Empty<DetectionEntity>();
    }

    public UnetSegmentationResult Result { get; }
    public IReadOnlyList<DetectionEntity> Detections { get; }
}
