using OpenCvSharp;

namespace VideoInferenceDemo.Tests.Pipeline;

public sealed class PipelineResultDispatchCoordinatorTests
{
    [Fact]
    public void TryPublish_SendsVisionFrameResult_ToConfiguredVisionSink()
    {
        var coordinator = new PipelineResultDispatchCoordinator();
        var sink = new CapturingVisionResultSink();
        coordinator.SetVisionSink(sink);
        using var mat = new Mat(3, 5, MatType.CV_8UC3);
        using var packet = new FramePacket(mat, 123, 123, 456, 7, static _ => { });

        coordinator.TryPublish(
            packet,
            new StubVisionTask("hand-task", VisionTaskKind.HandLandmarks),
            new HandLandmarksPayload(Array.Empty<HandLandmarkSet>()),
            "session-a",
            "camera:0",
            VideoSourceType.Camera,
            "run-a",
            1000,
            "model-v1");

        var result = Assert.Single(sink.Results);
        Assert.Equal("session-a", result.SessionId);
        Assert.Equal("hand-task", result.TaskId);
        Assert.Equal(VisionTaskKind.HandLandmarks, result.TaskKind);
        Assert.Equal("camera:0", result.Frame.SourceId);
        Assert.Equal("camera", result.Frame.SourceType);
        Assert.Equal("run-a", result.Frame.RunUuid);
        Assert.Equal(7, result.Frame.FrameIndex);
        Assert.Equal(123, result.Frame.TimestampMs);
        Assert.Equal(5, result.Frame.Width);
        Assert.Equal(3, result.Frame.Height);
        Assert.Equal("model-v1", result.Frame.ModelVersion);
    }

    private sealed class CapturingVisionResultSink : IVisionResultSink
    {
        public List<VisionFrameResult> Results { get; } = new();

        public bool TryPublish(VisionFrameResult result)
        {
            Results.Add(result);
            return true;
        }
    }

    private sealed class StubVisionTask : IVisionTask
    {
        public StubVisionTask(string taskId, VisionTaskKind taskKind)
        {
            TaskId = taskId;
            TaskKind = taskKind;
        }

        public string TaskId { get; }
        public VisionTaskKind TaskKind { get; }
        public VisionRuntimeKind RuntimeKind => VisionRuntimeKind.MediaPipe;
        public string? ActiveDeviceLabel => "MediaPipe / Runtime";

        public VisionTaskExecutionResult Execute(Mat image, VisionTaskExecutionContext context)
        {
            throw new NotSupportedException();
        }

        public void Warmup(int width, int height)
        {
        }

        public void UpdateClassNames(string[]? classNames)
        {
        }

        public bool TryHandleFailure(Exception ex, out string message)
        {
            message = ex.Message;
            return false;
        }

        public void Dispose()
        {
        }
    }
}
