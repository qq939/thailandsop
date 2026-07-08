namespace VideoInferenceDemo;

public sealed class SequenceBandsPayload : VisionTaskPayload
{
    public SequenceBandsPayload(
        IReadOnlyList<SequenceBandPrediction> bands,
        IReadOnlyList<DetectionEntity> detections)
    {
        Bands = bands ?? Array.Empty<SequenceBandPrediction>();
        Detections = detections ?? Array.Empty<DetectionEntity>();
    }

    public IReadOnlyList<SequenceBandPrediction> Bands { get; }
    public IReadOnlyList<DetectionEntity> Detections { get; }
}
