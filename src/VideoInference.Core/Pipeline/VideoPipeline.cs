using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace VideoInferenceDemo;

public sealed partial class VideoPipeline : IDisposable
{
    private readonly CameraProviderRegistry _cameraProviders;
    private readonly MatPool _matPool = new(6);
    private readonly bool _useFfmpegForVideo = true;
    private readonly PipelinePacketQueueCoordinator _packetQueues = new();
    private readonly PipelinePlaybackState _playback = new();
    private readonly PipelineRunCoordinator _runCoordinator = new();
    private double _targetFps;
    private VideoSourceType _sourceType;
    private bool _useSourcePtsForVideo = true;
    private CameraRecordingOptions _cameraRecordingOptions = CameraRecordingOptions.Disabled;

    private readonly PipelineTelemetryState _telemetry = new();
    private readonly PipelineTaskRuntimeState _taskRuntime = new();
    private readonly PipelineResultDispatchCoordinator _resultDispatchCoordinator = new();
    private readonly PipelineRecorderCoordinator _recorderCoordinator;
    private readonly PipelineDrawStyleState _drawStyle = new();
    private readonly PipelinePerformanceTracker _performance = new();
    private readonly PipelineCaptureCoordinator _captureCoordinator;
    private readonly PipelineInferenceRenderCoordinator _inferenceRenderCoordinator;
    private readonly PipelineFrameExecutionRequestFactory _frameExecutionRequestFactory;
    private readonly PipelineTaskExecutionCoordinator _taskExecutionCoordinator;
    private string _sourceId = "unknown";
    private string _runUuid = string.Empty;
    private long _runStartedUtcMs;
    public event Action<RenderPacket>? FrameReady;
    public event Action<PipelineStats>? StatsUpdated;
    public event Action<string>? Error;
    public event Action<string>? DeviceChanged;
    public event Action<PipelineRunEnded>? RunEnded;

    public string CurrentRunUuid => _runUuid;
    public long CurrentRunStartedUtcMs => Interlocked.Read(ref _runStartedUtcMs);

    public VideoPipeline(CameraProviderRegistry cameraProviders)
    {
        _cameraProviders = cameraProviders;
        _recorderCoordinator = new PipelineRecorderCoordinator(message => Error?.Invoke(message));
        _captureCoordinator = new PipelineCaptureCoordinator(_cameraProviders, _matPool, _packetQueues, _telemetry, _performance);
        _inferenceRenderCoordinator = new PipelineInferenceRenderCoordinator(_packetQueues, _telemetry, _performance);
        _frameExecutionRequestFactory = new PipelineFrameExecutionRequestFactory(GetDrawStyleSnapshot);
        _taskExecutionCoordinator = new PipelineTaskExecutionCoordinator(
            _performance,
            _resultDispatchCoordinator,
            message => Error?.Invoke(message),
            EmitDeviceChanged);
    }

    public void StartVideo(string path, double targetFps, bool useSourcePtsForVideo = true)
    {
        StartInternal(new VideoSource(VideoSourceType.VideoFile, path, null, useSourcePtsForVideo), targetFps);
    }

    public void StartCamera(CameraOpenOptions options, double targetFps, CameraRecordingOptions? recordingOptions = null)
    {
        _cameraRecordingOptions = recordingOptions ?? CameraRecordingOptions.Disabled;
        StartInternal(new VideoSource(VideoSourceType.Camera, string.Empty, options.Normalize(), false), targetFps);
    }

    public void StartCamera(int cameraIndex, double targetFps, CameraRecordingOptions? recordingOptions = null)
    {
        StartCamera(new CameraOpenOptions(CameraProviderIds.OpenCv, cameraIndex, null, targetFps), targetFps, recordingOptions);
    }

    public void Stop()
    {
        if (!_runCoordinator.Stop(
                Resume,
                () =>
                {
                    _packetQueues.CompleteFrameAdding();
                    _packetQueues.CompleteRenderAdding();
                }))
        {
            return;
        }

        StopRecorder();
        _packetQueues.Stop();
        _matPool.Clear();
    }

    public void RequestRecordingRotate(string? reason = null)
    {
        _recorderCoordinator.RequestRotate(reason);
    }

    public void Pause()
    {
        _playback.Pause();
    }

    public void Resume()
    {
        _playback.Resume();
    }

    public void UpdateClassNames(string[]? classNames)
    {
        _taskRuntime.UpdateClassNames(classNames);
    }

    public void UpdateDetectionDrawOptions(string? boxColor, string[]? boxColors, int? boxThickness, double? labelFontScale)
    {
        _drawStyle.Update(boxColor, boxColors, boxThickness, labelFontScale);
    }

    public void SetVisionResultSink(IVisionResultSink? sink)
    {
        _resultDispatchCoordinator.SetVisionSink(sink);
    }

    public void Warmup(int width = 640, int height = 640)
    {
        _taskRuntime.WarmupPrimary(width, height, EmitDeviceChanged);
    }

    public VisionWorkerStatusSnapshot? GetPrimaryWorkerStatus()
    {
        return _taskRuntime.GetPrimaryWorkerStatus();
    }

    public void UpdateTargetFps(double targetFps)
    {
        _targetFps = targetFps;
    }

    public void Dispose()
    {
        Stop();
        _runCoordinator.Dispose();
        _taskRuntime.Dispose();
        _playback.Dispose();
    }

    public void SetPrimaryTask(IVisionTask task, string? modelVersion = null, bool clearSidecars = false)
    {
        _taskRuntime.SetPrimaryTask(task, modelVersion, clearSidecars);
    }

    public void SetSidecarTasks(IEnumerable<IVisionTask> tasks)
    {
        _taskRuntime.SetSidecarTasks(tasks);
    }

    public void ClearSidecarTasks()
    {
        _taskRuntime.ClearSidecarTasks();
    }

    public void ClearTasks()
    {
        _taskRuntime.ClearTasks();
    }

    private void StartInternal(VideoSource source, double targetFps)
    {
        Stop();

        _targetFps = targetFps;
        _sourceType = source.Type;
        _useSourcePtsForVideo = source.Type == VideoSourceType.VideoFile && source.UseSourcePtsForVideo;
        _playback.Start(source.Type);
        if (source.Type != VideoSourceType.Camera)
        {
            _cameraRecordingOptions = CameraRecordingOptions.Disabled;
        }
        _sourceId = source.Type == VideoSourceType.VideoFile ? source.Path : BuildCameraSourceId(source.CameraOptions);
        _runUuid = Guid.NewGuid().ToString("N");
        Interlocked.Exchange(ref _runStartedUtcMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _telemetry.Reset();
        _performance.Reset();
        _packetQueues.Start();
        _ = _runCoordinator.Start(
            _runUuid,
            ct => CaptureLoop(source, ct),
            InferLoop,
            RenderLoop,
            _packetQueues.CompleteFrameAdding,
            _packetQueues.CompleteRenderAdding,
            ended => RunEnded?.Invoke(ended));
    }

    private bool CaptureLoop(VideoSource source, CancellationToken ct)
    {
        if (source.Type == VideoSourceType.VideoFile)
        {
            if (_useFfmpegForVideo)
            {
                try
                {
                    return _captureCoordinator.RunVideoWithFfmpeg(
                        source.Path,
                        _targetFps,
                        _useSourcePtsForVideo,
                        WaitIfPaused,
                        _playback.PaceToPts,
                        ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    CameraDiagnostics.Warn("pipeline-video", $"FFmpeg video playback failed, falling back to OpenCV. Path={source.Path}, Error={ex.Message}");
                    Error?.Invoke($"FFmpeg decode failed, fallback to OpenCV: {ex.Message}");
                }
            }

            return _captureCoordinator.RunVideoWithOpenCv(
                source.Path,
                _targetFps,
                _useSourcePtsForVideo,
                WaitIfPaused,
                _playback.PaceToPts,
                message => Error?.Invoke(message),
                ct);
        }

        return _captureCoordinator.RunCamera(
            source,
            _targetFps,
            _cameraRecordingOptions,
            sourceId => _sourceId = sourceId,
            StartRecorder,
            TryEnqueueRecordingFrame,
            message => Error?.Invoke(message),
            WaitIfPaused,
            ct);
    }

    private void WaitIfPaused(CancellationToken ct)
    {
        _playback.WaitIfPaused(ct);
    }

    private static string BuildCameraSourceId(CameraOpenOptions? options)
    {
        var normalized = (options ?? new CameraOpenOptions(CameraProviderIds.OpenCv, 0, null, 0)).Normalize();
        return $"{normalized.ProviderId}:{CameraOptionHelpers.GetSelector(normalized)}";
    }

}

public enum VideoSourceType
{
    VideoFile,
    Camera
}

public sealed record VideoSource(VideoSourceType Type, string Path, CameraOpenOptions? CameraOptions, bool UseSourcePtsForVideo = true);

public enum PipelineRunEndReason
{
    SourceEnded,
    SourceError,
    Canceled
}

public sealed record PipelineRunEnded(string RunUuid, PipelineRunEndReason Reason);
