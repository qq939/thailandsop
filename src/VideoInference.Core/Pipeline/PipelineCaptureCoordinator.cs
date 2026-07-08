using System.Diagnostics;
using System.Threading;
using OpenCvSharp;

namespace VideoInferenceDemo;

internal sealed class PipelineCaptureCoordinator
{
    private readonly CameraProviderRegistry _cameraProviders;
    private readonly MatPool _matPool;
    private readonly PipelinePacketQueueCoordinator _packetQueues;
    private readonly PipelineTelemetryState _telemetry;
    private readonly PipelinePerformanceTracker _performance;

    public PipelineCaptureCoordinator(
        CameraProviderRegistry cameraProviders,
        MatPool matPool,
        PipelinePacketQueueCoordinator packetQueues,
        PipelineTelemetryState telemetry,
        PipelinePerformanceTracker performance)
    {
        _cameraProviders = cameraProviders ?? throw new ArgumentNullException(nameof(cameraProviders));
        _matPool = matPool ?? throw new ArgumentNullException(nameof(matPool));
        _packetQueues = packetQueues ?? throw new ArgumentNullException(nameof(packetQueues));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _performance = performance ?? throw new ArgumentNullException(nameof(performance));
    }

    public bool RunVideoWithFfmpeg(
        string path,
        double targetFps,
        bool useSourcePtsForVideo,
        Action<CancellationToken> waitIfPaused,
        Action<double, CancellationToken> paceToPts,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(waitIfPaused);
        ArgumentNullException.ThrowIfNull(paceToPts);

        using var source = new FfmpegFrameSource(path, _matPool);
        var fpsCounter = new FpsCounter();
        var sequence = 0;
        var frameIndex = 0;
        var hostClock = Stopwatch.StartNew();
        double? firstPts = null;
        var reachedSourceEnd = false;

        var sourceFps = source.SourceFps;
        var fallbackIntervalMs = sourceFps > 0 ? 1000.0 / sourceFps : 0.0;
        var intervalMs = 0.0;
        var nextTargetPts = 0.0;

        _telemetry.SetSourceFps(sourceFps);
        _telemetry.SetSourceDurationMs(source.SourceDurationMs);

        while (!ct.IsCancellationRequested)
        {
            waitIfPaused(ct);

            var captureStartedAt = Stopwatch.GetTimestamp();
            if (!source.TryReadFrame(out var frame, out var ptsMs))
            {
                _performance.RecordCapture(Stopwatch.GetElapsedTime(captureStartedAt).TotalMilliseconds);
                reachedSourceEnd = true;
                break;
            }

            _performance.RecordCapture(Stopwatch.GetElapsedTime(captureStartedAt).TotalMilliseconds);
            var effectivePts = NormalizeVideoPts(
                ptsMs,
                frameIndex,
                fallbackIntervalMs,
                ref targetFps,
                ref intervalMs,
                ref nextTargetPts,
                ref firstPts);
            frameIndex++;

            UpdateObservedSourceFps(frameIndex, effectivePts);

            if (!TryAcceptVideoFrameByPts(effectivePts, ref targetFps, ref intervalMs, ref nextTargetPts))
            {
                _telemetry.IncrementDroppedByPts();
                _matPool.Release(frame);
                continue;
            }

            paceToPts(effectivePts, ct);
            waitIfPaused(ct);
            var sourcePtsMs = (long)effectivePts;
            var hostPtsMs = hostClock.ElapsedMilliseconds;
            var timestampMs = useSourcePtsForVideo ? sourcePtsMs : hostPtsMs;
            _telemetry.SetCurrentPtsMs(sourcePtsMs);

            var packet = new FramePacket(frame, timestampMs, sourcePtsMs, 0, sequence++, _matPool.Release);
            if (!_packetQueues.TryEnqueueCapturedFrameOrdered(packet, ct))
            {
                packet.Dispose();
                break;
            }

            _telemetry.SetCaptureFps(fpsCounter.Update(sourcePtsMs));
        }

        return reachedSourceEnd;
    }

    public bool RunVideoWithOpenCv(
        string path,
        double targetFps,
        bool useSourcePtsForVideo,
        Action<CancellationToken> waitIfPaused,
        Action<double, CancellationToken> paceToPts,
        Action<string> reportError,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(waitIfPaused);
        ArgumentNullException.ThrowIfNull(paceToPts);
        ArgumentNullException.ThrowIfNull(reportError);

        var reachedSourceEnd = false;
        try
        {
            using var capture = new VideoCapture(path);

            if (!capture.IsOpened())
            {
                reportError("Failed to open video source.");
                return false;
            }

            var hostClock = Stopwatch.StartNew();
            var fpsCounter = new FpsCounter();
            var sequence = 0;
            var frameIndex = 0;

            var sourceFps = capture.Get(VideoCaptureProperties.Fps);
            var fallbackIntervalMs = sourceFps > 0 ? 1000.0 / sourceFps : 0.0;
            var intervalMs = 0.0;
            var nextTargetPts = 0.0;
            double? firstPts = null;

            var frameCount = capture.Get(VideoCaptureProperties.FrameCount);
            var durationMs = sourceFps > 0 && frameCount > 0 ? (long)(1000.0 * frameCount / sourceFps) : 0;
            _telemetry.SetSourceFps(sourceFps);
            _telemetry.SetSourceDurationMs(durationMs);

            while (!ct.IsCancellationRequested)
            {
                waitIfPaused(ct);

                var frame = _matPool.Acquire();
                var captureStartedAt = Stopwatch.GetTimestamp();
                if (!capture.Read(frame) || frame.Empty())
                {
                    _performance.RecordCapture(Stopwatch.GetElapsedTime(captureStartedAt).TotalMilliseconds);
                    _matPool.Release(frame);
                    reachedSourceEnd = true;
                    break;
                }

                _performance.RecordCapture(Stopwatch.GetElapsedTime(captureStartedAt).TotalMilliseconds);

                var ptsMs = capture.Get(VideoCaptureProperties.PosMsec);
                var effectivePts = NormalizeVideoPts(
                    ptsMs,
                    frameIndex,
                    fallbackIntervalMs,
                    ref targetFps,
                    ref intervalMs,
                    ref nextTargetPts,
                    ref firstPts);
                frameIndex++;

                UpdateObservedSourceFps(frameIndex, effectivePts);

                if (!TryAcceptVideoFrameByPts(effectivePts, ref targetFps, ref intervalMs, ref nextTargetPts))
                {
                    _telemetry.IncrementDroppedByPts();
                    _matPool.Release(frame);
                    continue;
                }

                paceToPts(effectivePts, ct);
                var timelineMs = (long)effectivePts;
                waitIfPaused(ct);
                var hostPtsMs = hostClock.ElapsedMilliseconds;
                var timestampMs = useSourcePtsForVideo ? timelineMs : hostPtsMs;
                _telemetry.SetCurrentPtsMs(timelineMs);

                var packet = new FramePacket(frame, timestampMs, timelineMs, 0, sequence++, _matPool.Release);
                if (!_packetQueues.TryEnqueueCapturedFrameOrdered(packet, ct))
                {
                    packet.Dispose();
                    break;
                }

                _telemetry.SetCaptureFps(fpsCounter.Update(timelineMs));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            CameraDiagnostics.Info("pipeline-video", "OpenCV video playback canceled.");
        }
        catch (Exception ex)
        {
            reportError($"Capture error: {ex.Message}");
        }

        return reachedSourceEnd;
    }

    public bool RunCamera(
        VideoSource source,
        double targetFps,
        CameraRecordingOptions recordingOptions,
        Action<string> setSourceId,
        Action<int, int, double> startRecorder,
        Action<Mat, long, long> enqueueRecordingFrame,
        Action<string> reportError,
        Action<CancellationToken> waitIfPaused,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(setSourceId);
        ArgumentNullException.ThrowIfNull(startRecorder);
        ArgumentNullException.ThrowIfNull(enqueueRecordingFrame);
        ArgumentNullException.ThrowIfNull(reportError);
        ArgumentNullException.ThrowIfNull(waitIfPaused);

        var recorderStarted = false;
        try
        {
            var cameraOptions = (source.CameraOptions ?? new CameraOpenOptions(CameraProviderIds.OpenCv, 0, null, targetFps)).Normalize();
            var cameraSelector = CameraOptionHelpers.GetSelector(cameraOptions);
            CameraDiagnostics.Info(
                "pipeline-camera",
                $"Starting capture loop. Provider={cameraOptions.ProviderId}, Selector={cameraSelector}, TargetFps={cameraOptions.TargetFps:F2}");
            using var session = _cameraProviders.Open(cameraOptions);
            setSourceId(session.SourceId);
            CameraDiagnostics.Info(
                "pipeline-camera",
                $"Capture session opened. SourceId={session.SourceId}, DisplayName={session.DisplayName}, ReportedFps={session.ReportedFps:F2}");

            var startupClock = Stopwatch.StartNew();
            var fpsCounter = new FpsCounter();
            var sequence = 0;
            var sourceFps = session.ReportedFps > 0 ? session.ReportedFps : targetFps;
            var firstFrameArrived = false;
            _telemetry.SetSourceFps(sourceFps);
            _telemetry.SetSourceDurationMs(0);

            while (!ct.IsCancellationRequested)
            {
                waitIfPaused(ct);

                var frame = _matPool.Acquire();
                var captureStartedAt = Stopwatch.GetTimestamp();
                if (!session.TryCapture(frame, ct, out var metadata) || frame.Empty())
                {
                    _performance.RecordCapture(Stopwatch.GetElapsedTime(captureStartedAt).TotalMilliseconds);
                    _matPool.Release(frame);
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    if (!firstFrameArrived && startupClock.ElapsedMilliseconds >= 5000)
                    {
                        var timeoutMessage =
                            $"Camera start timed out after 5000 ms without receiving the first frame. Provider={cameraOptions.ProviderId}, Selector={cameraSelector}, DisplayName={session.DisplayName}, SourceId={session.SourceId}.";
                        CameraDiagnostics.Error("pipeline-camera", timeoutMessage);
                        reportError($"{timeoutMessage} See camera_debug.log for details.");
                        return false;
                    }

                    Thread.Sleep(1);
                    continue;
                }

                ApplyCameraTransform(frame, cameraOptions.Rotation, cameraOptions.MirrorMode);

                _performance.RecordCapture(Stopwatch.GetElapsedTime(captureStartedAt).TotalMilliseconds);

                if (!firstFrameArrived)
                {
                    firstFrameArrived = true;
                    CameraDiagnostics.Info(
                        "pipeline-camera",
                        $"First frame received. SourceId={session.SourceId}, PtsMs={metadata.PtsMs}, CaptureUtcMs={metadata.CaptureUtcMs}, PtsSource={metadata.PtsSource}");
                }

                var timelineMs = metadata.PtsMs;
                waitIfPaused(ct);
                _telemetry.SetCurrentPtsMs(timelineMs);

                if (recordingOptions.Enabled)
                {
                    if (!recorderStarted)
                    {
                        var recordingFps = recordingOptions.ResolveRecordingFps(targetFps, sourceFps);
                        startRecorder(frame.Width, frame.Height, recordingFps);
                        recorderStarted = true;
                    }

                    enqueueRecordingFrame(frame, metadata.PtsMs, metadata.CaptureUtcMs);
                }

                var packet = new FramePacket(frame, metadata.PtsMs, timelineMs, metadata.CaptureUtcMs, sequence++, _matPool.Release);
                _packetQueues.EnqueueCapturedFrameDropOldest(packet, _telemetry.IncrementDroppedByCaptureQueue);

                _telemetry.SetCaptureFps(fpsCounter.Update(timelineMs));
            }
        }
        catch (OperationCanceledException)
        {
            CameraDiagnostics.Info("pipeline-camera", "Camera capture loop canceled.");
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Error("pipeline-camera", "Camera capture loop failed.", ex);
            reportError($"Camera capture failed: {ex.Message}. See camera_debug.log for details.");
        }

        return false;
    }

    internal static void ApplyCameraTransform(Mat image, CameraRotation rotation, CameraMirrorMode mirrorMode)
    {
        if (rotation != CameraRotation.None)
        {
            var flags = rotation switch
            {
                CameraRotation.Rotate90 => RotateFlags.Rotate90Clockwise,
                CameraRotation.Rotate270 => RotateFlags.Rotate90Counterclockwise,
                _ => RotateFlags.Rotate180
            };
            Cv2.Rotate(image, image, flags);
        }

        if (mirrorMode != CameraMirrorMode.None)
        {
            var flipMode = mirrorMode switch
            {
                CameraMirrorMode.Horizontal => FlipMode.Y,
                CameraMirrorMode.Vertical => FlipMode.X,
                _ => FlipMode.XY
            };
            Cv2.Flip(image, image, flipMode);
        }
    }

    private bool TryAcceptVideoFrameByPts(double effectivePts, ref double targetFps, ref double intervalMs, ref double nextTargetPts)
    {
        RefreshTargetInterval(ref targetFps, ref intervalMs, ref nextTargetPts);
        return intervalMs <= 0 || ShouldAcceptByPts(effectivePts, intervalMs, ref nextTargetPts);
    }

    private static double NormalizeVideoPts(
        double ptsMs,
        int frameIndex,
        double fallbackIntervalMs,
        ref double targetFps,
        ref double intervalMs,
        ref double nextTargetPts,
        ref double? firstPts)
    {
        if (ptsMs <= 0)
        {
            var effectiveFallback = fallbackIntervalMs;
            if (effectiveFallback <= 0)
            {
                RefreshTargetInterval(ref targetFps, ref intervalMs, ref nextTargetPts);
                effectiveFallback = intervalMs;
            }

            ptsMs = frameIndex * (effectiveFallback > 0 ? effectiveFallback : 0.0);
        }

        if (!firstPts.HasValue)
        {
            firstPts = ptsMs;
        }

        var effectivePts = ptsMs - firstPts.Value;
        return effectivePts < 0 ? 0 : effectivePts;
    }

    private void UpdateObservedSourceFps(int frameIndex, double effectivePtsMs)
    {
        if (frameIndex <= 1 || effectivePtsMs <= 0)
        {
            return;
        }

        _telemetry.SetSourceFps((frameIndex - 1) * 1000.0 / effectivePtsMs);
    }

    private static void RefreshTargetInterval(ref double targetFps, ref double intervalMs, ref double nextTargetPts)
    {
        var newIntervalMs = targetFps > 0 ? 1000.0 / targetFps : 0.0;
        if (Math.Abs(newIntervalMs - intervalMs) > 0.01)
        {
            intervalMs = newIntervalMs;
            nextTargetPts = 0.0;
        }
    }

    private static bool ShouldAcceptByPts(double ptsMs, double intervalMs, ref double nextTargetPts)
    {
        if (intervalMs <= 0)
        {
            return true;
        }

        if (nextTargetPts <= 0)
        {
            nextTargetPts = ptsMs;
        }

        if (ptsMs + 0.001 < nextTargetPts)
        {
            return false;
        }

        while (ptsMs >= nextTargetPts + intervalMs)
        {
            nextTargetPts += intervalMs;
        }

        nextTargetPts += intervalMs;
        return true;
    }
}
