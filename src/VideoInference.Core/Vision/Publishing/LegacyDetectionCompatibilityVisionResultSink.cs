namespace VideoInferenceDemo;

public sealed class LegacyDetectionCompatibilityVisionResultSink : IVisionResultSink
{
    private readonly ILegacyDetectionResultSink _sink;

    public LegacyDetectionCompatibilityVisionResultSink(ILegacyDetectionResultSink sink)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    public bool TryPublish(VisionFrameResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Payload switch
        {
            DetectionPayload detectionPayload => _sink.TryEnqueue(
                new FrameDetections(result.Frame, detectionPayload.Detections, result.TaskId, result.TaskKind)),
            SequenceBandsPayload sequenceBandsPayload => _sink.TryEnqueue(
                new FrameDetections(result.Frame, sequenceBandsPayload.Detections, result.TaskId, result.TaskKind)),
            _ => true
        };
    }
}
