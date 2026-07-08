namespace VideoInferenceDemo.Tests.Vision.Workers;

public sealed class NamedPipeWorkerRuntimePolicyTests
{
    [Fact]
    public void OnStartCompleted_MarksTrackerReady_WithRuntimeLabel()
    {
        var tracker = new NamedPipeWorkerRuntimeStateTracker(
            "MediaPipe",
            "endpoint-a",
            VisionWorkerProtocolKind.NamedPipe);
        var policy = new NamedPipeWorkerRuntimePolicy("endpoint-a", tracker);

        policy.OnStartInitiated();
        policy.OnStartCompleted("MediaPipe / CPU");

        Assert.Equal(VisionWorkerState.Ready, tracker.State);
        Assert.Equal("MediaPipe / CPU", tracker.ActiveRuntimeLabel);
    }

    [Fact]
    public void OnPingFailed_MarksTrackerDegraded()
    {
        var tracker = new NamedPipeWorkerRuntimeStateTracker(
            "MediaPipe",
            "endpoint-a",
            VisionWorkerProtocolKind.NamedPipe);
        var policy = new NamedPipeWorkerRuntimePolicy("endpoint-a", tracker);

        policy.OnStartInitiated();
        policy.OnStartCompleted("MediaPipe / CPU");
        policy.OnPingFailed();

        Assert.Equal(VisionWorkerState.Degraded, tracker.State);
    }

    [Fact]
    public void OnUnexpectedExit_RecordsEndpointSpecificFault()
    {
        var tracker = new NamedPipeWorkerRuntimeStateTracker(
            "OCR",
            "endpoint-b",
            VisionWorkerProtocolKind.NamedPipe);
        var policy = new NamedPipeWorkerRuntimePolicy("endpoint-b", tracker);

        policy.OnUnexpectedExit(27);
        var snapshot = tracker.CreateSnapshot(processId: null);

        Assert.Equal(VisionWorkerState.Faulted, tracker.State);
        Assert.Equal(27, snapshot.ExitCode);
        Assert.Contains("endpoint-b", snapshot.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public void OnRequestFailed_DoesNotRestoreReadyState_WhenRequestAlreadyFaulted()
    {
        var tracker = new NamedPipeWorkerRuntimeStateTracker(
            "MediaPipe",
            "endpoint-c",
            VisionWorkerProtocolKind.NamedPipe);
        var policy = new NamedPipeWorkerRuntimePolicy("endpoint-c", tracker);

        policy.OnStartInitiated();
        policy.OnStartCompleted("MediaPipe / CPU");
        var previousState = policy.OnRequestStarted();
        policy.OnRequestFailed(new InvalidOperationException("request failed"));
        policy.OnRequestFinished(previousState);

        Assert.Equal(VisionWorkerState.Faulted, tracker.State);
        var snapshot = tracker.CreateSnapshot(processId: null);
        Assert.Contains("request failed", snapshot.LastError, StringComparison.Ordinal);
    }
}
