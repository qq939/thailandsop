using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Toolboxs.Extensions;
using Sdcb.FFmpeg.Utils;
using OpenCvSharp;

namespace VideoInferenceDemo;

public sealed class FfmpegFrameSource : IDisposable
{
    private readonly FormatContext _formatContext;
    private readonly MediaStream _videoStream;
    private readonly CodecContext _codecContext;
    private readonly IEnumerator<Frame> _frameEnumerator;
    private readonly Frame _bgrFrame = new();
    private readonly VideoFrameConverter _converter = new();
    private readonly MatPool _matPool;
    private bool _bgrReady;
    private readonly double _sourceFps;
    private readonly long _sourceDurationMs;

    public FfmpegFrameSource(string path, MatPool matPool)
    {
        _matPool = matPool;
        _formatContext = FormatContext.OpenInputUrl(path, null, new MediaDictionary());
        _formatContext.LoadStreamInfo();

        _videoStream = FindVideoStream(_formatContext);
        var codecParams = _videoStream.Codecpar ?? throw new InvalidOperationException("Stream codec parameters missing.");
        var codec = Codec.FindDecoderById(codecParams.CodecId);

        _codecContext = new CodecContext(codec);
        _codecContext.FillParameters(codecParams);
        _codecContext.Open(codec, new MediaDictionary());

        _frameEnumerator = _formatContext
            .ReadPackets(new[] { _videoStream.Index })
            .DecodePackets(_codecContext)
            .GetEnumerator();

        _sourceFps = ResolveFps(_videoStream);
        _sourceDurationMs = ResolveDurationMs(_formatContext, _videoStream);
    }

    public double SourceFps => _sourceFps;
    public long SourceDurationMs => _sourceDurationMs;

    public bool TryReadFrame(out Mat mat, out double ptsMs)
    {
        mat = default!;
        ptsMs = 0;

        while (_frameEnumerator.MoveNext())
        {
            var frame = _frameEnumerator.Current;
            try
            {
                EnsureBgrFrame(frame);
                _converter.ConvertFrame(frame, _bgrFrame, SWS.Bilinear);

                var dst = _matPool.Acquire();
                dst.Create(_bgrFrame.Height, _bgrFrame.Width, MatType.CV_8UC3);
                using (var src = Mat.FromPixelData(_bgrFrame.Height, _bgrFrame.Width, MatType.CV_8UC3, _bgrFrame.Data[0], _bgrFrame.Linesize[0]))
                {
                    src.CopyTo(dst);
                }

                ptsMs = ToMs(frame);
                mat = dst;
                return true;
            }
            finally
            {
                frame.Unref();
            }
        }

        return false;
    }

    public void Dispose()
    {
        _bgrFrame.Free();
        _frameEnumerator.Dispose();
        _codecContext.Dispose();
        _formatContext.Dispose();
        _converter.Free();
    }

    private static MediaStream FindVideoStream(FormatContext context)
    {
        foreach (var stream in context.Streams)
        {
            var codecpar = stream.Codecpar;
            if (codecpar != null && codecpar.CodecType == AVMediaType.Video)
            {
                return stream;
            }
        }

        throw new InvalidOperationException("No video stream found.");
    }

    private void EnsureBgrFrame(Frame source)
    {
        if (_bgrReady)
        {
            return;
        }

        _bgrFrame.Width = source.Width;
        _bgrFrame.Height = source.Height;
        _bgrFrame.Format = (int)AVPixelFormat.Bgr24;
        _bgrFrame.EnsureBuffer(1);
        _bgrReady = true;
    }

    private double ToMs(Frame frame)
    {
        var pts = frame.BestEffortTimestamp;
        if (pts == 0)
        {
            pts = frame.Pts;
        }

        if (pts == 0)
        {
            return 0;
        }

        var timeBase = _videoStream.TimeBase;
        if (timeBase.Den == 0)
        {
            timeBase = frame.TimeBase;
        }

        return pts * timeBase.Num * 1000.0 / timeBase.Den;
    }

    private static double ResolveFps(MediaStream stream)
    {
        var fps = RationalToDouble(stream.AvgFrameRate);
        if (fps > 0)
        {
            return fps;
        }

        fps = RationalToDouble(stream.RFrameRate);
        return fps;
    }

    private static long ResolveDurationMs(FormatContext context, MediaStream stream)
    {
        try
        {
            var durationSec = stream.GetDurationInSeconds();
            if (durationSec > 0 && !double.IsNaN(durationSec) && !double.IsInfinity(durationSec))
            {
                return (long)(durationSec * 1000.0);
            }
        }
        catch
        {
        }

        try
        {
            const double avTimeBase = 1_000_000.0;
            if (context.Duration > 0)
            {
                return (long)(context.Duration * 1000.0 / avTimeBase);
            }
        }
        catch
        {
        }

        return 0;
    }

    private static double RationalToDouble(AVRational rational)
    {
        if (rational.Den == 0)
        {
            return 0;
        }

        return rational.Num / (double)rational.Den;
    }
}
