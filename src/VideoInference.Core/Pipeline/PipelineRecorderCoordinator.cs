using System;
using OpenCvSharp;

namespace VideoInferenceDemo;

internal sealed class PipelineRecorderCoordinator : IDisposable
{
    private readonly Action<string> _reportError;
    private IFrameRecorder? _recorder;

    public PipelineRecorderCoordinator(Action<string> reportError)
    {
        _reportError = reportError;
    }

    public void Start(CameraRecordingOptions options, int width, int height, double sourceFps)
    {
        Stop();

        try
        {
            var normalized = options.Normalize();
            if (!normalized.Enabled)
            {
                return;
            }

            _recorder = new SegmentedVideoRecorder(normalized, width, height, sourceFps);
            _recorder.Error += _reportError;
            _recorder.Start();
        }
        catch (Exception ex)
        {
            ReportError("Recorder start failed.", ex);
            Stop();
        }
    }

    public void TryEnqueue(Mat frame, long ptsMs, long captureUtcMs)
    {
        _recorder?.TryEnqueue(frame, ptsMs, captureUtcMs);
    }

    public void RequestRotate(string? reason)
    {
        _recorder?.RequestRotate(reason);
    }

    public void Stop()
    {
        var recorder = _recorder;
        _recorder = null;
        if (recorder == null)
        {
            return;
        }

        try
        {
            recorder.Stop();
        }
        catch (Exception ex)
        {
            ReportError("Recorder stop failed.", ex);
        }

        recorder.Dispose();
    }

    public void Dispose()
    {
        Stop();
    }

    private void ReportError(string message, Exception exception)
    {
        CameraDiagnostics.Error("recorder", message, exception);
        _reportError($"{message} {exception.Message}. See camera_debug.log for details.");
    }
}
