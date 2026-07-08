using System;

namespace VideoInferenceDemo;

public sealed class TcnLabelEntry
{
    public TcnLabelEntry(
        string runUuid,
        long runStartedUtcMs,
        string sourceKey,
        int? stepIndex,
        string label,
        long startMs,
        long endMs,
        string sourceType,
        float? score)
    {
        RunUuid = runUuid ?? string.Empty;
        RunStartedUtcMs = runStartedUtcMs;
        SourceKey = sourceKey ?? string.Empty;
        StepIndex = stepIndex;
        Label = string.IsNullOrWhiteSpace(label) ? "unknown" : label;
        StartMs = startMs;
        EndMs = endMs;
        SourceType = string.IsNullOrWhiteSpace(sourceType) ? "manual" : sourceType;
        Score = score;
    }

    public string RunUuid { get; }
    public long RunStartedUtcMs { get; }
    public string SourceKey { get; }
    public int? StepIndex { get; }
    public string Label { get; }
    public long StartMs { get; }
    public long EndMs { get; }
    public string SourceType { get; }
    public float? Score { get; }

    public long CreatedUtcMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public interface ITcnPredictionProvider
{
    bool TryGetCurrent(out string label, out float score);
}
