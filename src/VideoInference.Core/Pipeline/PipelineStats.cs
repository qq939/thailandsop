namespace VideoInferenceDemo;

public sealed record PipelineStats(
    double CaptureFps,
    double InferFps,
    double RenderFps,
    double SourceFps,
    int FrameQueueSize,
    int RenderQueueSize,
    long SourceDurationMs,
    long CurrentPtsMs,
    long DroppedByPts,
    long DroppedByCaptureQueue,
    long DroppedByInferDrain,
    long DroppedByRenderQueue,
    long DroppedByRenderDrain,
    PipelinePerformanceSnapshot Performance);
