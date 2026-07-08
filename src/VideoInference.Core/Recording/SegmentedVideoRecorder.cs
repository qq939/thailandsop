using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Utils;

namespace VideoInferenceDemo;

public sealed class SegmentedVideoRecorder : IFrameRecorder
{
    private readonly CameraRecordingOptions _options;
    private readonly int _width;
    private readonly int _height;
    private readonly double _sourceFps;
    private readonly BlockingCollection<RecordedFrame> _queue;
    private readonly long _segmentDurationMs;
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private long _dropped;
    private long _lastDropReportMs;
    private int _rotateRequested;
    private int _faulted;

    public SegmentedVideoRecorder(CameraRecordingOptions options, int width, int height, double sourceFps)
    {
        _options = options.Normalize();
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _sourceFps = sourceFps > 0 && !double.IsNaN(sourceFps) && !double.IsInfinity(sourceFps) ? sourceFps : 30;
        _queue = new BlockingCollection<RecordedFrame>(
            new ConcurrentQueue<RecordedFrame>(),
            _options.QueueCapacity);
        _segmentDurationMs = Math.Max(1, _options.SegmentMinutes) * 60L * 1000L;
    }

    public event Action<string>? Error;
    public long DroppedCount => Interlocked.Read(ref _dropped);

    public void Start()
    {
        if (_worker != null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Stop()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts == null)
        {
            return;
        }

        try
        {
            _queue.CompleteAdding();
        }
        catch (Exception ex)
        {
            ReportError("Recorder stop failed while completing the frame queue.", ex);
        }

        try
        {
            if (_worker != null && !_worker.Wait(TimeSpan.FromSeconds(30)))
            {
                ReportError("Recorder stop timed out while draining. Cancelling worker.");
                cts.Cancel();
                _worker.Wait(TimeSpan.FromSeconds(5));
            }
        }
        catch (Exception ex)
        {
            ReportError("Recorder stop failed while waiting for the worker.", ex);
        }

        cts.Dispose();
        _worker = null;
    }

    public bool TryEnqueue(Mat frame, long ptsMs, long captureUtcMs)
    {
        if (_queue.IsAddingCompleted)
        {
            Interlocked.Increment(ref _dropped);
            ReportDroppedFrame("recorder queue has already stopped");
            return false;
        }

        if (Volatile.Read(ref _faulted) != 0)
        {
            Interlocked.Increment(ref _dropped);
            ReportDroppedFrame("recorder worker has failed");
            return false;
        }

        Mat cloned;
        try
        {
            cloned = frame.Clone();
        }
        catch (Exception ex)
        {
            ReportError("Recorder clone failed.", ex);
            return false;
        }

        if (_queue.TryAdd(new RecordedFrame(cloned, ptsMs, captureUtcMs)))
        {
            return true;
        }

        cloned.Dispose();
        Interlocked.Increment(ref _dropped);
        ReportDroppedFrame("recorder queue is full");
        return false;
    }

    public void RequestRotate(string? reason = null)
    {
        Interlocked.Exchange(ref _rotateRequested, 1);
    }

    public void Dispose()
    {
        Stop();
        _queue.Dispose();
    }

    private void RunAsync(CancellationToken ct)
    {
        SegmentWriter? writer = null;
        long segmentStartPts = -1;
        DateTime? currentDay = null;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (_queue.IsCompleted)
                {
                    break;
                }

                if (!_queue.TryTake(out var frame, 50))
                {
                    continue;
                }

                using (frame)
                {
                    if (writer == null || ShouldRotate(frame, segmentStartPts, currentDay))
                    {
                        writer?.Dispose();
                        writer = OpenWriter(frame.CaptureUtcMs);
                        segmentStartPts = frame.PtsMs;
                        currentDay = ResolveLocalTime(frame.CaptureUtcMs).Date;
                        Interlocked.Exchange(ref _rotateRequested, 0);
                    }

                    writer.Write(frame.Frame, frame.PtsMs);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _faulted, 1);
            ReportError("Recorder worker failed.", ex);
        }
        finally
        {
            try
            {
                _queue.CompleteAdding();
            }
            catch (Exception ex)
            {
                ReportError("Recorder failed while closing the frame queue.", ex);
            }

            while (_queue.TryTake(out var left))
            {
                left.Dispose();
            }

            if (writer != null)
            {
                try
                {
                    writer.Dispose();
                }
                catch (Exception ex)
                {
                    ReportError("Recorder failed while closing the current segment.", ex);
                }
            }
        }
    }

    private bool ShouldRotate(RecordedFrame frame, long segmentStartPts, DateTime? currentDay)
    {
        if (Interlocked.Exchange(ref _rotateRequested, 0) == 1)
        {
            return true;
        }

        if (segmentStartPts >= 0 && frame.PtsMs - segmentStartPts >= _segmentDurationMs)
        {
            return true;
        }

        var localDate = ResolveLocalTime(frame.CaptureUtcMs).Date;
        return currentDay.HasValue && localDate != currentDay.Value;
    }

    private SegmentWriter OpenWriter(long captureUtcMs)
    {
        var now = ResolveLocalTime(captureUtcMs);
        var dayDir = Path.Combine(_options.RootDirectory, now.ToString("yyyy-MM-dd"), _options.CameraName);
        Directory.CreateDirectory(dayDir);
        var path = BuildSegmentPath(dayDir, now, _options.ResolveFileExtension());
        return new SegmentWriter(path, _options, _width, _height, _sourceFps);
    }

    private static string BuildSegmentPath(string dayDir, DateTimeOffset now, string extension)
    {
        var baseName = now.ToString("yyyyMMdd_HHmmss");
        var path = Path.Combine(dayDir, $"{baseName}.{extension}");
        if (!File.Exists(path))
        {
            return path;
        }

        for (var i = 1; i <= 99; i++)
        {
            var candidate = Path.Combine(dayDir, $"{baseName}_{i:00}.{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(dayDir, $"{baseName}_{Guid.NewGuid():N}.{extension}");
    }

    private void ReportDroppedFrame(string reason)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastDropReportMs);
        if (now - last < 5000)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastDropReportMs, now, last) == last)
        {
            ReportError($"Recorder dropped frame because {reason}. DroppedCount={DroppedCount}.");
        }
    }

    private void ReportError(string message, Exception? exception = null)
    {
        CameraDiagnostics.Error("recorder", message, exception);
        Error?.Invoke(exception == null
            ? message
            : $"{message} {exception.Message}. See camera_debug.log for details.");
    }

    private static DateTimeOffset ResolveLocalTime(long captureUtcMs)
    {
        return captureUtcMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(captureUtcMs).ToLocalTime()
            : DateTimeOffset.Now;
    }

    private sealed class SegmentWriter : IDisposable
    {
        private readonly FormatContext _formatContext;
        private readonly IOContext _ioContext;
        private readonly CodecContext _encoder;
        private readonly MediaStream _stream;
        private readonly VideoFrameConverter _converter = new();
        private readonly Frame _bgrFrame = new();
        private readonly Frame _yuvFrame = new();
        private readonly Packet _packet = new();
        private readonly AVRational _timeBase;
        private readonly Queue<FrameTiming> _pendingTimings = new();
        private bool _disposed;
        private readonly int _recordingFps;
        private long _lastPtsMs = -1;
        private long _writtenFrameCount;
        private int _packetLogCounter;

        public SegmentWriter(string path, CameraRecordingOptions options, int width, int height, double sourceFps)
        {
            _recordingFps = Math.Max(1, (int)Math.Round(sourceFps > 0 ? sourceFps : 30));
            _timeBase = new AVRational { Num = 1, Den = _recordingFps };
            var outputFormat = OutputFormat.Guess(null, path, null);
            _formatContext = FormatContext.AllocOutput(outputFormat, null, path);
            _formatContext.AvoidNegativeTs = (int)AVFMT_AVOID_NEG_TS.MakeZero;
            _ioContext = IOContext.Open(path, AVIO_FLAG.Write, new MediaDictionary());
            _formatContext.Pb = _ioContext;

            var codec = Codec.FindEncoderByName(options.VideoEncoder)
                ?? throw new InvalidOperationException($"FFmpeg encoder '{options.VideoEncoder}' was not found.");

            _stream = _formatContext.NewStream(codec);
            _stream.TimeBase = _timeBase;
            _encoder = new CodecContext(codec)
            {
                Width = width,
                Height = height,
                TimeBase = _timeBase,
                PktTimebase = _timeBase,
                Framerate = new AVRational
                {
                    Num = _recordingFps,
                    Den = 1
                },
                PixelFormat = AVPixelFormat.Yuv420p,
                GopSize = _recordingFps,
                MaxBFrames = 0,
                BitRate = Math.Max(1, options.BitrateMbps) * 1_000_000L
            };

            if ((((int)(_formatContext.OutputFormat?.Flags ?? 0)) & (int)AVFMT.Globalheader) != 0)
            {
                _encoder.Flags |= AV_CODEC_FLAG.GlobalHeader;
            }

            var encoderOptions = new MediaDictionary
            {
                ["preset"] = "p4",
                ["rc"] = "vbr"
            };
            _encoder.Open(codec, encoderOptions);
            _stream.Codecpar!.CopyFrom(_encoder);

            _bgrFrame.Width = width;
            _bgrFrame.Height = height;
            _bgrFrame.Format = (int)AVPixelFormat.Bgr24;
            _bgrFrame.EnsureBuffer(1);

            _yuvFrame.Width = width;
            _yuvFrame.Height = height;
            _yuvFrame.Format = (int)AVPixelFormat.Yuv420p;
            _yuvFrame.EnsureBuffer(1);

            _formatContext.WriteHeader(new MediaDictionary());
            CameraDiagnostics.Info(
                "recorder",
                $"Opened recorder. Path={path}, Encoder={options.VideoEncoder}, Container={options.ContainerFormat}, Size={width}x{height}, RecordingFps={_recordingFps}, InputFps={sourceFps:F2}, BitrateMbps={options.BitrateMbps}, EncoderTimeBase={FormatTimeBase(_encoder.TimeBase)}, StreamTimeBase={FormatTimeBase(_stream.TimeBase)}");
        }

        public void Write(Mat frame, long ptsMs)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SegmentWriter));
            }

            var normalizedPts = ResolveConstantFrameRatePts();
            _bgrFrame.MakeWritable();
            _yuvFrame.MakeWritable();
            CopyMatToBgrFrame(frame, _bgrFrame);
            _converter.ConvertFrame(_bgrFrame, _yuvFrame, SWS.Bilinear);
            _yuvFrame.Pts = normalizedPts;
            _yuvFrame.TimeBase = _timeBase;
            _yuvFrame.Duration = _lastPtsMs >= 0
                ? Math.Max(1, normalizedPts - _lastPtsMs)
                : 1;

            _pendingTimings.Enqueue(new FrameTiming(normalizedPts, _yuvFrame.Duration));
            _encoder.SendFrame(_yuvFrame);
            DrainPackets();
            _lastPtsMs = normalizedPts;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Exception? closeFailure = null;
            try
            {
                _encoder.SendFrame(null!);
                DrainPackets();
            }
            catch (Exception ex)
            {
                closeFailure = ex;
            }

            try
            {
                _formatContext.WriteTrailer();
            }
            catch (Exception ex)
            {
                closeFailure = closeFailure == null
                    ? ex
                    : new AggregateException(closeFailure, ex);
            }

            _packet.Dispose();
            _yuvFrame.Dispose();
            _bgrFrame.Dispose();
            _converter.Dispose();
            _encoder.Dispose();
            _ioContext.Dispose();
            _formatContext.Dispose();

            if (closeFailure != null)
            {
                throw new InvalidOperationException("Recorder segment close failed.", closeFailure);
            }
        }

        private void DrainPackets()
        {
            while (true)
            {
                var receiveResult = _encoder.ReceivePacket(_packet);
                if (receiveResult == CodecResult.Again || receiveResult == CodecResult.EOF)
                {
                    return;
                }

                if (receiveResult != CodecResult.Success)
                {
                    CameraDiagnostics.Info("recorder-packet", $"ReceivePacket returned {receiveResult}.");
                    return;
                }

                try
                {
                    var packetCount = Interlocked.Increment(ref _packetLogCounter);
                    ApplyPacketTiming(packetCount);
                    _packet.StreamIndex = _stream.Index;
                    _packet.RescaleTimestamp(_timeBase, _stream.TimeBase);
                    if (packetCount <= 5 || _packet.Pts == ffmpeg.AV_NOPTS_VALUE || _packet.Dts == ffmpeg.AV_NOPTS_VALUE)
                    {
                        CameraDiagnostics.Info(
                            "recorder-packet",
                            $"Packet#{packetCount} pts={_packet.Pts}, dts={_packet.Dts}, duration={_packet.Duration}, sourceTimeBase={FormatTimeBase(_timeBase)}, encoderTimeBase={FormatTimeBase(_encoder.TimeBase)}, pending={_pendingTimings.Count}");
                    }
                    _formatContext.InterleavedWritePacket(_packet);
                }
                finally
                {
                    _packet.Unref();
                }
            }
        }

        private void ApplyPacketTiming(int packetCount)
        {
            if (_pendingTimings.Count == 0)
            {
                if (packetCount <= 5)
                {
                    CameraDiagnostics.Info(
                        "recorder-packet",
                        $"Packet#{packetCount} has no pending frame timing. pts={_packet.Pts}, dts={_packet.Dts}, duration={_packet.Duration}");
                }

                return;
            }

            var timing = _pendingTimings.Dequeue();
            var encoderPts = _packet.Pts;
            var encoderDts = _packet.Dts;
            var encoderDuration = _packet.Duration;
            _packet.Pts = timing.Pts;
            _packet.Dts = timing.Pts;
            _packet.Duration = timing.Duration;

            if (packetCount <= 5)
            {
                CameraDiagnostics.Info(
                    "recorder-packet",
                    $"Packet#{packetCount} frame timing applied. pts={_packet.Pts}, dts={_packet.Dts}, duration={_packet.Duration}, encoderPts={encoderPts}, encoderDts={encoderDts}, encoderDuration={encoderDuration}");
            }
        }

        private long ResolveConstantFrameRatePts()
        {
            return _writtenFrameCount++;
        }

        private static void CopyMatToBgrFrame(Mat source, Frame destination)
        {
            using var continuousClone = source.IsContinuous() ? null : source.Clone();
            var continuous = continuousClone ?? source;
            using var target = Mat.FromPixelData(
                destination.Height,
                destination.Width,
                MatType.CV_8UC3,
                (nint)destination.Data[0],
                destination.Linesize[0]);
            continuous.CopyTo(target);
        }

        private static string FormatTimeBase(AVRational timeBase) => $"{timeBase.Num}/{timeBase.Den}";

        private readonly record struct FrameTiming(long Pts, long Duration);
    }

    private sealed class RecordedFrame : IDisposable
    {
        public RecordedFrame(Mat frame, long ptsMs, long captureUtcMs)
        {
            Frame = frame;
            PtsMs = ptsMs;
            CaptureUtcMs = captureUtcMs;
        }

        public Mat Frame { get; }
        public long PtsMs { get; }
        public long CaptureUtcMs { get; }

        public void Dispose()
        {
            Frame.Dispose();
        }
    }
}
