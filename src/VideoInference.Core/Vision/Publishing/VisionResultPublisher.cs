namespace VideoInferenceDemo;

internal sealed class VisionResultPublisher
{
    private IVisionResultSink? _sink;

    public void SetSink(IVisionResultSink? sink)
    {
        _sink = sink;
    }

    public void TryPublish(
        FramePacket packet,
        VisionTaskPayload payload,
        string sessionId,
        string taskId,
        VisionTaskKind taskKind,
        string sourceId,
        VideoSourceType sourceType,
        string runUuid,
        long runStartedUtcMs,
        string? modelVersion)
    {
        var sink = _sink;
        if (sink == null)
        {
            return;
        }

        var frameUtcMs = packet.CaptureUtcMs > 0
            ? packet.CaptureUtcMs
            : runStartedUtcMs > 0
                ? runStartedUtcMs + Math.Max(0, packet.PtsMs)
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var frame = new FrameEntity
        {
            SourceId = sourceId,
            SourceType = sourceType == VideoSourceType.Camera ? "camera" : "video",
            RunUuid = runUuid,
            RunStartedUtcMs = runStartedUtcMs,
            FrameIndex = packet.Sequence,
            TimestampMs = packet.PtsMs,
            FrameUtcMs = frameUtcMs,
            InferenceTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Width = packet.Image.Width,
            Height = packet.Image.Height,
            ModelVersion = modelVersion
        };

        sink.TryPublish(new VisionFrameResult(
            frame,
            sessionId,
            taskId,
            taskKind,
            payload));
    }
}
