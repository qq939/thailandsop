namespace VideoInferenceDemo;

internal sealed class PipelineResultDispatchCoordinator
{
    private readonly VisionResultPublisher _visionPublisher = new();

    public void SetVisionSink(IVisionResultSink? sink)
    {
        _visionPublisher.SetSink(sink);
    }

    public void TryPublish(
        FramePacket packet,
        IVisionTask task,
        VisionTaskPayload payload,
        string sessionId,
        string sourceId,
        VideoSourceType sourceType,
        string runUuid,
        long runStartedUtcMs,
        string? modelVersion)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(payload);

        _visionPublisher.TryPublish(
            packet,
            payload,
            sessionId,
            task.TaskId,
            task.TaskKind,
            sourceId,
            sourceType,
            runUuid,
            runStartedUtcMs,
            modelVersion);
    }

}
