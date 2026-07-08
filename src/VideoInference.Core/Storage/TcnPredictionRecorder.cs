using System;
using System.Collections.Generic;

namespace VideoInferenceDemo;

public sealed class TcnPredictionRecorder : IDisposable
{
    private sealed class SegmentState
    {
        public string RunUuid = string.Empty;
        public long RunStartedUtcMs;
        public string SourceKey = string.Empty;
        public string Label = string.Empty;
        public long StartMs;
        public long LastMs;
        public double ScoreSum;
        public int Count;
    }

    private readonly TcnOnnxInferenceEngine _engine;
    private readonly TcnLabelWriter _writer;
    private readonly Dictionary<string, SegmentState> _segments = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private bool _disposed;

    public TcnPredictionRecorder(TcnOnnxInferenceEngine engine, TcnLabelWriter writer)
    {
        _engine = engine;
        _writer = writer;
        _engine.PredictionReady += OnPrediction;
    }

    public void Flush()
    {
        lock (_lock)
        {
            foreach (var kvp in _segments)
            {
                CloseSegment(kvp.Value);
            }

            _segments.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _engine.PredictionReady -= OnPrediction;
        Flush();
    }

    private void OnPrediction(TcnPrediction prediction)
    {
        if (_disposed)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(prediction.SourceKey) || string.IsNullOrWhiteSpace(prediction.RunUuid))
        {
            return;
        }

        lock (_lock)
        {
            if (!_segments.TryGetValue(prediction.RunUuid, out var segment))
            {
                segment = new SegmentState
                {
                    RunUuid = prediction.RunUuid,
                    RunStartedUtcMs = prediction.RunStartedUtcMs,
                    SourceKey = prediction.SourceKey,
                    Label = prediction.Label,
                    StartMs = prediction.PtsMs,
                    LastMs = prediction.PtsMs,
                    ScoreSum = prediction.Score,
                    Count = 1
                };
                _segments[prediction.RunUuid] = segment;
                return;
            }

            if (prediction.PtsMs < segment.LastMs)
            {
                return;
            }

            if (string.Equals(segment.Label, prediction.Label, StringComparison.OrdinalIgnoreCase))
            {
                segment.LastMs = prediction.PtsMs;
                segment.ScoreSum += prediction.Score;
                segment.Count++;
                return;
            }

            CloseSegment(segment);

            segment.RunUuid = prediction.RunUuid;
            segment.RunStartedUtcMs = prediction.RunStartedUtcMs;
            segment.SourceKey = prediction.SourceKey;
            segment.Label = prediction.Label;
            segment.StartMs = prediction.PtsMs;
            segment.LastMs = prediction.PtsMs;
            segment.ScoreSum = prediction.Score;
            segment.Count = 1;
        }
    }

    private void CloseSegment(SegmentState segment)
    {
        if (segment.Count <= 0)
        {
            return;
        }

        var endMs = segment.LastMs;
        if (endMs <= segment.StartMs)
        {
            endMs = segment.StartMs + 1;
        }

        var avgScore = (float)(segment.ScoreSum / Math.Max(1, segment.Count));
        var entry = new TcnLabelEntry(
            segment.RunUuid,
            segment.RunStartedUtcMs,
            segment.SourceKey,
            null,
            segment.Label,
            segment.StartMs,
            endMs,
            "tcn",
            avgScore);

        _writer.TryEnqueue(entry);
    }
}
