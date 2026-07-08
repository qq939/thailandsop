using System;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace VideoInferenceDemo;

public sealed class RenderPacket : IDisposable
{
    private Action<Mat>? _release;

    public RenderPacket(Mat image, long timelineMs, int sequence, Action<Mat> release)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(release);

        Image = image;
        Width = image.Width;
        Height = image.Height;
        Stride = (int)image.Step();
        var bufferSize = checked(Stride * Height);
        PixelBuffer = new byte[bufferSize];
        Marshal.Copy(image.Data, PixelBuffer, 0, bufferSize);
        TimelineMs = timelineMs;
        Sequence = sequence;
        _release = release;
    }

    public Mat Image { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public byte[] PixelBuffer { get; }
    public long TimelineMs { get; }
    public int Sequence { get; }

    public void Dispose()
    {
        var release = _release;
        _release = null;
        release?.Invoke(Image);
    }
}
