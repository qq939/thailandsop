namespace VideoInferenceDemo;

public enum InferenceModelKind
{
    YoloDetection,
    YoloObbDetection,
    YoloSegmentation,
    UnetSegmentation,
    PresenceClassification,
    SequenceBands,
    TcnClassification
}

public sealed class InferenceModelDescriptor
{
    public required ModelCatalogEntry Model { get; init; }
    public required InferenceModelKind ModelKind { get; init; }
    public required InferenceDeviceKind DeviceKind { get; init; }
    public required string ModelPath { get; init; }
    public string[]? ClassNames { get; init; }
    public float ConfidenceThreshold { get; init; }
    public float NmsThreshold { get; init; }
    public SequenceModelMetadata? SequenceMetadata { get; init; }
    public YoloDetectionMetadata? YoloMetadata { get; init; }
    public YoloObbDetectionMetadata? YoloObbMetadata { get; init; }
    public YoloSegmentationMetadata? YoloSegmentationMetadata { get; init; }
    public UnetSegmentationMetadata? UnetSegmentationMetadata { get; init; }
    public PresenceClassificationMetadata? PresenceClassificationMetadata { get; init; }
}
