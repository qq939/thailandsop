namespace VideoInferenceDemo.Tests.Vision.Workers;

public sealed class NamedPipeWorkerRuntimeStateTrackerTests
{
    [Fact]
    public void CreateSnapshot_UsesNamedPipeFallbackRuntimeLabel_ByDefault()
    {
        var tracker = new NamedPipeWorkerRuntimeStateTracker(
            "MediaPipe",
            "worker-1",
            VisionWorkerProtocolKind.NamedPipe);

        var snapshot = tracker.CreateSnapshot(processId: 42);

        Assert.Equal(VisionWorkerState.Created, snapshot.State);
        Assert.Equal("Worker / NamedPipe", snapshot.RuntimeLabel);
        Assert.Equal(42, snapshot.ProcessId);
    }

    [Fact]
    public void CompleteRequest_StoresRuntimeLabel_AndReturnsReadyState()
    {
        var tracker = new NamedPipeWorkerRuntimeStateTracker(
            "MediaPipe",
            "worker-1",
            VisionWorkerProtocolKind.NamedPipe);

        tracker.ResetForStart();
        tracker.MarkReady();
        var previousState = tracker.MarkBusy();
        tracker.CompleteRequest(isSuccess: true, runtimeLabel: "MediaPipe / GPU");
        tracker.RestoreAfterBusy(previousState);

        Assert.Equal(VisionWorkerState.Ready, tracker.State);
        Assert.Equal("MediaPipe / GPU", tracker.ActiveRuntimeLabel);
    }

    [Fact]
    public void RecordProcessExit_CapturesExitCodeAndFaultMessage()
    {
        var tracker = new NamedPipeWorkerRuntimeStateTracker(
            "OCR",
            "worker-2",
            VisionWorkerProtocolKind.NamedPipe);

        tracker.AppendStdErr("trace line");
        tracker.RecordProcessExit(13, "worker crashed");
        var snapshot = tracker.CreateSnapshot(processId: null);

        Assert.Equal(VisionWorkerState.Faulted, tracker.State);
        Assert.Equal(13, tracker.LastExitCode);
        Assert.Contains("worker crashed", snapshot.LastError, StringComparison.Ordinal);
        Assert.Equal("trace line", snapshot.LastStdErr);
    }
}
