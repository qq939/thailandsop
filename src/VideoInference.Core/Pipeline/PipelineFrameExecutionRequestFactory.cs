namespace VideoInferenceDemo;

internal sealed class PipelineFrameExecutionRequestFactory
{
    private readonly Func<PipelineDrawStyle> _getDrawStyle;

    public PipelineFrameExecutionRequestFactory(Func<PipelineDrawStyle> getDrawStyle)
    {
        _getDrawStyle = getDrawStyle ?? throw new ArgumentNullException(nameof(getDrawStyle));
    }

    public PipelineFrameExecutionRequest Create(
        FramePacket packet,
        PipelineTaskRuntimeSnapshot taskSnapshot,
        string sourceId,
        VideoSourceType sourceType,
        string runUuid,
        long runStartedUtcMs)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(taskSnapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runUuid);

        return new PipelineFrameExecutionRequest(
            packet,
            packet.Image,
            taskSnapshot.PrimaryTask,
            taskSnapshot.SidecarTasks,
            BuildExecutionContext(packet),
            new PipelineExecutionMetadata(
                sourceId,
                sourceId,
                sourceType,
                runUuid,
                runStartedUtcMs,
                taskSnapshot.ModelVersion));
    }

    private VisionTaskExecutionContext BuildExecutionContext(FramePacket packet)
    {
        var drawStyle = _getDrawStyle();
        return new VisionTaskExecutionContext(
            new SessionFrameContext(
                packet.PtsMs,
                packet.TimelineMs,
                packet.CaptureUtcMs,
                packet.Sequence,
                packet.Image.Width,
                packet.Image.Height),
            new VisionTaskRenderStyle(
                drawStyle.GlobalOverride,
                drawStyle.OverridesByClass,
                drawStyle.BoxThickness,
                drawStyle.LabelFontScale));
    }
}
