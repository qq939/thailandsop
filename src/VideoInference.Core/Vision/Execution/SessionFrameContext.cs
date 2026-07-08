namespace VideoInferenceDemo;

public sealed record SessionFrameContext(
    long PtsMs,
    long TimelineMs,
    long CaptureUtcMs,
    int Sequence,
    int Width,
    int Height);
