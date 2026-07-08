namespace VideoInferenceDemo;

public sealed class AnalysisConfig
{
    public bool EnableOnlineAnalysis { get; set; }
    public string? Strategy { get; set; }
    public string? SourceTaskId { get; set; }
    public int FrameWindowSize { get; set; }
    public int StateWindowSize { get; set; }
    public int NearThresholdQ1000 { get; set; }
    public int? NearStep { get; set; }
    public int HoldFrames { get; set; }
    public int SopWindowMs { get; set; }
    public int SopMinScoreQ1000 { get; set; }
    public int SopMinVisibleRatioQ1000 { get; set; }
    public float ConfidenceThreshold { get; set; }
}
