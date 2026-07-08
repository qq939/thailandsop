using System;
using System.Threading;

namespace VideoInferenceDemo;

internal sealed class PipelineTelemetryState
{
    private double _captureFps;
    private double _inferFps;
    private double _renderFps;
    private double _sourceFpsMeta;
    private long _lastStatsMs;
    private long _sourceDurationMs;
    private long _currentPtsMs;
    private long _droppedByPts;
    private long _droppedByCaptureQueue;
    private long _droppedByInferDrain;
    private long _droppedByRenderQueue;
    private long _droppedByRenderDrain;

    public void Reset()
    {
        Volatile.Write(ref _captureFps, 0);
        Volatile.Write(ref _inferFps, 0);
        Volatile.Write(ref _renderFps, 0);
        Volatile.Write(ref _sourceFpsMeta, 0);
        Interlocked.Exchange(ref _sourceDurationMs, 0);
        Interlocked.Exchange(ref _currentPtsMs, 0);
        Interlocked.Exchange(ref _droppedByPts, 0);
        Interlocked.Exchange(ref _droppedByCaptureQueue, 0);
        Interlocked.Exchange(ref _droppedByInferDrain, 0);
        Interlocked.Exchange(ref _droppedByRenderQueue, 0);
        Interlocked.Exchange(ref _droppedByRenderDrain, 0);
        Interlocked.Exchange(ref _lastStatsMs, 0);
    }

    public void SetCaptureFps(double value) => Volatile.Write(ref _captureFps, value);
    public void SetInferFps(double value) => Volatile.Write(ref _inferFps, value);
    public void SetRenderFps(double value) => Volatile.Write(ref _renderFps, value);
    public void SetSourceFps(double value) => Volatile.Write(ref _sourceFpsMeta, value);
    public void SetSourceDurationMs(long value) => Interlocked.Exchange(ref _sourceDurationMs, value);
    public void SetCurrentPtsMs(long value) => Volatile.Write(ref _currentPtsMs, value);

    public void IncrementDroppedByPts() => Interlocked.Increment(ref _droppedByPts);
    public void IncrementDroppedByCaptureQueue() => Interlocked.Increment(ref _droppedByCaptureQueue);
    public void IncrementDroppedByInferDrain() => Interlocked.Increment(ref _droppedByInferDrain);
    public void IncrementDroppedByRenderQueue() => Interlocked.Increment(ref _droppedByRenderQueue);
    public void IncrementDroppedByRenderDrain() => Interlocked.Increment(ref _droppedByRenderDrain);

    public bool ShouldEmitStats(bool force)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastStatsMs);
        if (!force && now - last < 250)
        {
            return false;
        }

        Interlocked.Exchange(ref _lastStatsMs, now);
        return true;
    }

    public PipelineStats Snapshot(int frameQueueSize, int renderQueueSize, PipelinePerformanceSnapshot performance)
    {
        return new PipelineStats(
            Volatile.Read(ref _captureFps),
            Volatile.Read(ref _inferFps),
            Volatile.Read(ref _renderFps),
            Volatile.Read(ref _sourceFpsMeta),
            frameQueueSize,
            renderQueueSize,
            Interlocked.Read(ref _sourceDurationMs),
            Interlocked.Read(ref _currentPtsMs),
            Interlocked.Read(ref _droppedByPts),
            Interlocked.Read(ref _droppedByCaptureQueue),
            Interlocked.Read(ref _droppedByInferDrain),
            Interlocked.Read(ref _droppedByRenderQueue),
            Interlocked.Read(ref _droppedByRenderDrain),
            performance);
    }
}
