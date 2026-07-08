namespace VideoInferenceDemo;

public sealed record PipelineStageTiming(
    double AverageMs,
    double MaxMs,
    int SampleCount)
{
    public static PipelineStageTiming Empty { get; } = new(0, 0, 0);
}

public sealed record PipelinePerformanceSnapshot(
    PipelineStageTiming Capture,
    PipelineStageTiming Infer,
    PipelineStageTiming ModelPreprocess,
    PipelineStageTiming ModelOrtRun,
    PipelineStageTiming ModelPostprocess,
    PipelineStageTiming ModelTotal,
    PipelineStageTiming Annotate,
    PipelineStageTiming RenderPresent,
    PipelineStageTiming RecordEnqueue)
{
    public static PipelinePerformanceSnapshot Empty { get; } = new(
        PipelineStageTiming.Empty,
        PipelineStageTiming.Empty,
        PipelineStageTiming.Empty,
        PipelineStageTiming.Empty,
        PipelineStageTiming.Empty,
        PipelineStageTiming.Empty,
        PipelineStageTiming.Empty,
        PipelineStageTiming.Empty,
        PipelineStageTiming.Empty);
}

internal sealed class PipelinePerformanceTracker
{
    private readonly RollingTimingTracker _capture = new();
    private readonly RollingTimingTracker _infer = new();
    private readonly RollingTimingTracker _modelPreprocess = new();
    private readonly RollingTimingTracker _modelOrtRun = new();
    private readonly RollingTimingTracker _modelPostprocess = new();
    private readonly RollingTimingTracker _modelTotal = new();
    private readonly RollingTimingTracker _annotate = new();
    private readonly RollingTimingTracker _renderPresent = new();
    private readonly RollingTimingTracker _recordEnqueue = new();

    public void Reset()
    {
        _capture.Reset();
        _infer.Reset();
        _modelPreprocess.Reset();
        _modelOrtRun.Reset();
        _modelPostprocess.Reset();
        _modelTotal.Reset();
        _annotate.Reset();
        _renderPresent.Reset();
        _recordEnqueue.Reset();
    }

    public void RecordCapture(double elapsedMs) => _capture.Add(elapsedMs);
    public void RecordInfer(double elapsedMs) => _infer.Add(elapsedMs);
    public void RecordAnnotate(double elapsedMs) => _annotate.Add(elapsedMs);
    public void RecordRenderPresent(double elapsedMs) => _renderPresent.Add(elapsedMs);
    public void RecordRecordEnqueue(double elapsedMs) => _recordEnqueue.Add(elapsedMs);

    public void RecordModelMetrics(IReadOnlyDictionary<string, string>? metrics)
    {
        if (metrics is null || metrics.Count == 0)
        {
            return;
        }

        TryRecordMetric(metrics, "preprocessMs", _modelPreprocess);
        TryRecordMetric(metrics, "ortRunMs", _modelOrtRun);
        TryRecordMetric(metrics, "postprocessMs", _modelPostprocess);
        TryRecordMetric(metrics, "modelTotalMs", _modelTotal);
    }

    public PipelinePerformanceSnapshot Snapshot()
    {
        return new PipelinePerformanceSnapshot(
            _capture.Snapshot(),
            _infer.Snapshot(),
            _modelPreprocess.Snapshot(),
            _modelOrtRun.Snapshot(),
            _modelPostprocess.Snapshot(),
            _modelTotal.Snapshot(),
            _annotate.Snapshot(),
            _renderPresent.Snapshot(),
            _recordEnqueue.Snapshot());
    }

    private static void TryRecordMetric(
        IReadOnlyDictionary<string, string> metrics,
        string key,
        RollingTimingTracker tracker)
    {
        if (!metrics.TryGetValue(key, out var value))
        {
            return;
        }

        if (double.TryParse(
            value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var elapsedMs))
        {
            tracker.Add(elapsedMs);
        }
    }

    private sealed class RollingTimingTracker
    {
        private const int Capacity = 32;
        private readonly object _sync = new();
        private readonly double[] _samples = new double[Capacity];
        private int _count;
        private int _nextIndex;

        public void Add(double elapsedMs)
        {
            lock (_sync)
            {
                _samples[_nextIndex] = elapsedMs;
                _nextIndex = (_nextIndex + 1) % Capacity;
                if (_count < Capacity)
                {
                    _count++;
                }
            }
        }

        public void Reset()
        {
            lock (_sync)
            {
                Array.Clear(_samples, 0, _samples.Length);
                _count = 0;
                _nextIndex = 0;
            }
        }

        public PipelineStageTiming Snapshot()
        {
            lock (_sync)
            {
                if (_count == 0)
                {
                    return PipelineStageTiming.Empty;
                }

                double total = 0;
                double max = 0;
                for (var i = 0; i < _count; i++)
                {
                    var sample = _samples[i];
                    total += sample;
                    if (sample > max)
                    {
                        max = sample;
                    }
                }

                return new PipelineStageTiming(total / _count, max, _count);
            }
        }
    }
}
