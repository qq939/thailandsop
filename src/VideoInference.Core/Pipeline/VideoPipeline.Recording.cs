using System.Diagnostics;

namespace VideoInferenceDemo;

public sealed partial class VideoPipeline
{
    private void StartRecorder(int width, int height, double sourceFps)
    {
        _recorderCoordinator.Start(_cameraRecordingOptions, width, height, sourceFps);
    }

    private void StopRecorder()
    {
        _recorderCoordinator.Stop();
    }

    private void TryEnqueueRecordingFrame(OpenCvSharp.Mat frame, long ptsMs, long captureUtcMs)
    {
        var startedAt = Stopwatch.GetTimestamp();
        _recorderCoordinator.TryEnqueue(frame, ptsMs, captureUtcMs);
        _performance.RecordRecordEnqueue(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
    }
}
