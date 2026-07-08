namespace VideoInferenceDemo.Tests.Vision.Publishing;

public sealed class LegacyDetectionCompatibilityVisionResultSinkTests
{
    [Fact]
    public void TryPublish_ConvertsDetectionPayload_ToLegacyFrameDetections()
    {
        var sink = new CapturingLegacyDetectionSink();
        var adapter = new LegacyDetectionCompatibilityVisionResultSink(sink);
        var detections = new[]
        {
            new DetectionEntity { ClassId = 1, Score = 0.8f, X1 = 1, Y1 = 2, X2 = 3, Y2 = 4 }
        };
        var result = new VisionFrameResult(
            new FrameEntity
            {
                SourceId = "camera:0",
                SourceType = "camera",
                RunUuid = "run-a",
                FrameIndex = 2
            },
            "session-a",
            "det-task",
            VisionTaskKind.Detection,
            new DetectionPayload(detections));

        var published = adapter.TryPublish(result);

        Assert.True(published);
        var batch = Assert.Single(sink.Batches);
        Assert.Same(result.Frame, batch.Frame);
        Assert.Equal("det-task", batch.TaskId);
        Assert.Equal(VisionTaskKind.Detection, batch.TaskKind);
        Assert.Single(batch.Detections);
        Assert.Equal(1, batch.Detections[0].ClassId);
    }

    [Fact]
    public void TryPublish_IgnoresNonLegacyPayload()
    {
        var sink = new CapturingLegacyDetectionSink();
        var adapter = new LegacyDetectionCompatibilityVisionResultSink(sink);
        var result = new VisionFrameResult(
            new FrameEntity
            {
                SourceId = "camera:0",
                SourceType = "camera",
                RunUuid = "run-a",
                FrameIndex = 2
            },
            "session-a",
            "hand-task",
            VisionTaskKind.HandLandmarks,
            new HandLandmarksPayload(Array.Empty<HandLandmarkSet>()));

        var published = adapter.TryPublish(result);

        Assert.True(published);
        Assert.Empty(sink.Batches);
    }

    private sealed class CapturingLegacyDetectionSink : ILegacyDetectionResultSink
    {
        public List<FrameDetections> Batches { get; } = new();

        public bool TryEnqueue(FrameDetections batch)
        {
            Batches.Add(batch);
            return true;
        }
    }
}
