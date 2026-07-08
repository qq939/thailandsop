using System.Globalization;

namespace VideoInferenceDemo;

public sealed record SessionMetricsSnapshot(
    double CaptureFps,
    double InferFps,
    double RenderFps,
    string SourceFpsText,
    string SourceDurationText,
    string PlaybackTimeText,
    string CurrentTimeText,
    int FrameQueueSize,
    int RenderQueueSize,
    long DroppedByPts,
    long DroppedByCaptureQueue,
    long DroppedByInferDrain,
    long DroppedByRenderQueue,
    long DroppedByRenderDrain,
    long CurrentPtsMs,
    string CaptureSummaryText,
    PipelinePerformanceSnapshot Performance)
{
    public static SessionMetricsSnapshot Empty { get; } = new(
        0,
        0,
        0,
        "-",
        "-",
        "-",
        "-",
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        "Capture 0.0",
        PipelinePerformanceSnapshot.Empty);
}

public static class SessionMetricsFormatter
{
    public static SessionMetricsSnapshot FromPipelineStats(PipelineStats stats, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(stats);

        return new SessionMetricsSnapshot(
            stats.CaptureFps,
            stats.InferFps,
            stats.RenderFps,
            stats.SourceFps > 0 ? stats.SourceFps.ToString("F2", CultureInfo.InvariantCulture) : "-",
            stats.SourceDurationMs > 0 ? FormatDuration(stats.SourceDurationMs) : "-",
            FormatDuration(stats.CurrentPtsMs),
            now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            stats.FrameQueueSize,
            stats.RenderQueueSize,
            stats.DroppedByPts,
            stats.DroppedByCaptureQueue,
            stats.DroppedByInferDrain,
            stats.DroppedByRenderQueue,
            stats.DroppedByRenderDrain,
            stats.CurrentPtsMs,
            $"Capture {stats.CaptureFps:F1}",
            stats.Performance);
    }

    public static string FormatDuration(long ms)
    {
        if (ms < 0)
        {
            return "-";
        }

        var ts = TimeSpan.FromMilliseconds(ms);
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
        }

        return $"{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
    }
}
