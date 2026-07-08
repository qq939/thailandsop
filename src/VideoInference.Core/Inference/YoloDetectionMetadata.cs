namespace VideoInferenceDemo;

public enum YoloOutputLayout
{
    Auto,
    ChannelsFirst,
    BoxesFirst
}

public enum YoloScoreMode
{
    Auto,
    ClassOnly,
    ObjectnessAndClass
}

public sealed class YoloDetectionMetadata
{
    public YoloOutputLayout OutputLayout { get; init; } = YoloOutputLayout.Auto;
    public YoloScoreMode ScoreMode { get; init; } = YoloScoreMode.Auto;
    public int? ClassCount { get; init; }
    public float MinScore { get; init; }
    public float NmsThreshold { get; init; }
    public string TensorRtCacheKey { get; init; } = string.Empty;
}

public sealed class YoloObbDetectionMetadata
{
    public YoloOutputLayout OutputLayout { get; init; } = YoloOutputLayout.Auto;
    public YoloScoreMode ScoreMode { get; init; } = YoloScoreMode.Auto;
    public int? ClassCount { get; init; }
    public float MinScore { get; init; }
    public float NmsThreshold { get; init; }
    public float LocatorMinScore { get; init; }
    public string TensorRtCacheKey { get; init; } = string.Empty;
}

public sealed class YoloSegmentationMetadata
{
    public YoloOutputLayout OutputLayout { get; init; } = YoloOutputLayout.Auto;
    public YoloScoreMode ScoreMode { get; init; } = YoloScoreMode.Auto;
    public int? ClassCount { get; init; }
    public string DetectionOutputName { get; init; } = string.Empty;
    public string PrototypeOutputName { get; init; } = string.Empty;
    public float MinScore { get; init; }
    public float NmsThreshold { get; init; }
    public float MaskThreshold { get; init; } = 0.5f;
    public string TensorRtCacheKey { get; init; } = string.Empty;
}
