using System.Diagnostics;
using OpenCvSharp;

namespace VideoInferenceDemo;

internal sealed class PipelineTaskExecutionCoordinator
{
    private readonly PipelinePerformanceTracker _performance;
    private readonly PipelineResultDispatchCoordinator _resultDispatchCoordinator;
    private readonly Action<string> _reportError;
    private readonly Action<string?> _emitDeviceChanged;

    public PipelineTaskExecutionCoordinator(
        PipelinePerformanceTracker performance,
        PipelineResultDispatchCoordinator resultDispatchCoordinator,
        Action<string> reportError,
        Action<string?> emitDeviceChanged)
    {
        _performance = performance ?? throw new ArgumentNullException(nameof(performance));
        _resultDispatchCoordinator = resultDispatchCoordinator ?? throw new ArgumentNullException(nameof(resultDispatchCoordinator));
        _reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
        _emitDeviceChanged = emitDeviceChanged ?? throw new ArgumentNullException(nameof(emitDeviceChanged));
    }

    public bool TryExecuteFrame(PipelineFrameExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var inferStartedAt = Stopwatch.GetTimestamp();
            var primaryResult = request.PrimaryTask.Execute(request.Image, request.ExecutionContext);
            _performance.RecordInfer(Stopwatch.GetElapsedTime(inferStartedAt).TotalMilliseconds);
            _performance.RecordModelMetrics(primaryResult.Metrics);

            var annotateStartedAt = Stopwatch.GetTimestamp();
            DispatchAndAnnotate(request.Packet, request.Image, request.PrimaryTask, primaryResult, request.Metadata);

            foreach (var sidecarTask in request.SidecarTasks)
            {
                var sidecarInferStartedAt = Stopwatch.GetTimestamp();
                var sidecarResult = sidecarTask.Execute(request.Image, request.ExecutionContext);
                _performance.RecordInfer(Stopwatch.GetElapsedTime(sidecarInferStartedAt).TotalMilliseconds);
                _performance.RecordModelMetrics(sidecarResult.Metrics);
                DispatchAndAnnotate(request.Packet, request.Image, sidecarTask, sidecarResult, request.Metadata);
            }

            _performance.RecordAnnotate(Stopwatch.GetElapsedTime(annotateStartedAt).TotalMilliseconds);
            return true;
        }
        catch (Exception ex)
        {
            HandleExecutionFailure(request.PrimaryTask, ex);
            return false;
        }
    }

    private void DispatchAndAnnotate(
        FramePacket packet,
        Mat image,
        IVisionTask task,
        VisionTaskExecutionResult result,
        PipelineExecutionMetadata metadata)
    {
        _resultDispatchCoordinator.TryPublish(
            packet,
            task,
            result.Payload,
            metadata.SessionId,
            metadata.SourceId,
            metadata.SourceType,
            metadata.RunUuid,
            metadata.RunStartedUtcMs,
            metadata.ModelVersion);
        result.Annotate(image);
        _emitDeviceChanged(result.DeviceLabel);
    }

    private void HandleExecutionFailure(IVisionTask primaryTask, Exception ex)
    {
        if (primaryTask.TryHandleFailure(ex, out var message))
        {
            _reportError(message);
            _emitDeviceChanged(primaryTask.ActiveDeviceLabel);
            return;
        }

        _reportError(message);
    }
}
