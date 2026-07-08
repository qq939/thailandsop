using OpenCvSharp;

namespace VideoInferenceDemo;

public sealed class FramePacket : IDisposable
{
    private Action<Mat>? _release;

    public FramePacket(Mat image, long ptsMs, long timelineMs, long captureUtcMs, int sequence, Action<Mat> release)
    {
        Image = image;
        PtsMs = ptsMs;
        TimelineMs = timelineMs;
        CaptureUtcMs = captureUtcMs;
        Sequence = sequence;
        _release = release;
    }

    public Mat Image { get; }
    public long PtsMs { get; }
    public long TimelineMs { get; }
    public long CaptureUtcMs { get; }
    public int Sequence { get; }

    public bool TryTakeOwnership(out Action<Mat>? release)
    {
        release = _release;
        _release = null;
        return release != null;
    }

    public void Dispose()
    {
        var release = _release;
        _release = null;
        release?.Invoke(Image);
    }
}
