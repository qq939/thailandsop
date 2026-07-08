using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace VideoInferenceDemo;

internal readonly record struct PipelineInferFrameResult(RenderPacket RenderPacket, bool TransferOwnership);

internal sealed class PipelineInferenceRenderCoordinator
{
    private readonly PipelinePacketQueueCoordinator _packetQueues;
    private readonly PipelineTelemetryState _telemetry;
    private readonly PipelinePerformanceTracker _performance;

    public PipelineInferenceRenderCoordinator(
        PipelinePacketQueueCoordinator packetQueues,
        PipelineTelemetryState telemetry,
        PipelinePerformanceTracker performance)
    {
        _packetQueues = packetQueues ?? throw new ArgumentNullException(nameof(packetQueues));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _performance = performance ?? throw new ArgumentNullException(nameof(performance));
    }

    public async Task RunInferLoop(
        VideoSourceType sourceType,
        Func<double> getTargetFps,
        Action<CancellationToken> waitIfPaused,
        Func<FramePacket, PipelineInferFrameResult> processFrame,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(getTargetFps);
        ArgumentNullException.ThrowIfNull(waitIfPaused);
        ArgumentNullException.ThrowIfNull(processFrame);

        var fpsGate = new FpsGate(getTargetFps);
        var fpsCounter = new FpsCounter();
        var useLossyQueue = sourceType == VideoSourceType.Camera;

        while (!ct.IsCancellationRequested)
        {
            waitIfPaused(ct);

            if (sourceType == VideoSourceType.Camera && getTargetFps() > 0)
            {
                try
                {
                    await fpsGate.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            FramePacket? packet = null;
            try
            {
                var hasPacket = _packetQueues.TryTakeFrame(
                    useLossyQueue,
                    out packet,
                    _telemetry.IncrementDroppedByInferDrain,
                    ct);
                if (!hasPacket || packet == null)
                {
                    if (_packetQueues.IsFrameQueueCompleted)
                    {
                        break;
                    }

                    continue;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }

            waitIfPaused(ct);
            var currentPacket = packet;
            var result = default(PipelineInferFrameResult);
            try
            {
                result = processFrame(currentPacket);
                if (!_packetQueues.TryEnqueueRender(
                        useLossyQueue,
                        result.RenderPacket,
                        _telemetry.IncrementDroppedByRenderQueue,
                        ct))
                {
                    result.RenderPacket.Dispose();
                    break;
                }
            }
            finally
            {
                if (!result.TransferOwnership)
                {
                    currentPacket.Dispose();
                }
            }

            _telemetry.SetInferFps(fpsCounter.Update(Environment.TickCount64));
        }
    }

    public void RunRenderLoop(
        VideoSourceType sourceType,
        Action<CancellationToken> waitIfPaused,
        Func<Action<RenderPacket>?> getFrameReady,
        Func<Action<PipelineStats>?> getStatsUpdated,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(waitIfPaused);
        ArgumentNullException.ThrowIfNull(getFrameReady);
        ArgumentNullException.ThrowIfNull(getStatsUpdated);

        var fpsCounter = new FpsCounter();
        var useLossyQueue = sourceType == VideoSourceType.Camera;

        while (!ct.IsCancellationRequested)
        {
            waitIfPaused(ct);

            RenderPacket? packet = null;
            try
            {
                var hasPacket = _packetQueues.TryTakeRender(
                    useLossyQueue,
                    out packet,
                    _telemetry.IncrementDroppedByRenderDrain,
                    ct);
                if (!hasPacket || packet == null)
                {
                    if (_packetQueues.IsRenderQueueCompleted)
                    {
                        break;
                    }

                    continue;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }

            waitIfPaused(ct);
            PresentFrame(packet, getFrameReady());
            _telemetry.SetRenderFps(fpsCounter.Update(Environment.TickCount64));

            EmitStatsIfNeeded(getStatsUpdated());
        }

        EmitStatsIfNeeded(getStatsUpdated(), force: true);
    }

    private void PresentFrame(RenderPacket packet, Action<RenderPacket>? frameReady)
    {
        var presentStartedAt = Stopwatch.GetTimestamp();
        if (frameReady != null)
        {
            frameReady.Invoke(packet);
        }
        else
        {
            packet.Dispose();
        }

        _performance.RecordRenderPresent(Stopwatch.GetElapsedTime(presentStartedAt).TotalMilliseconds);
    }

    private void EmitStatsIfNeeded(Action<PipelineStats>? statsUpdated, bool force = false)
    {
        if (statsUpdated == null || !_telemetry.ShouldEmitStats(force))
        {
            return;
        }

        statsUpdated.Invoke(_telemetry.Snapshot(
            _packetQueues.FrameQueueCount,
            _packetQueues.RenderQueueCount,
            _performance.Snapshot()));
    }
}
