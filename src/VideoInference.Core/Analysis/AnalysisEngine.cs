using System;
using System.Collections.Generic;
using NewLife.Log;

namespace VideoInferenceDemo;

public interface IFrameMetricsExtractor
{
    bool TryExtract(FrameDetections batch, out FsmFrameMetrics metrics);
}

public interface IAnalysisStrategy
{
    AnalysisResult Analyze(FsmFrameMetrics current, IReadOnlyList<FsmFrameMetrics> window, AnalysisContext context);
}

public sealed class FsmFrameMetricsExtractor : IFrameMetricsExtractor
{
    public bool TryExtract(FrameDetections batch, out FsmFrameMetrics metrics)
    {
        var features = FsmFeatureCalculator.Compute(batch);
        var frame = batch.Frame;
        var frameUtcMs = frame.FrameUtcMs > 0 ? frame.FrameUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        metrics = new FsmFrameMetrics
        {
            RunUuid = frame.RunUuid,
            RunStartedUtcMs = frame.RunStartedUtcMs,
            SourceKey = frame.SourceId,
            TaskId = batch.TaskId,
            TaskKind = batch.TaskKind,
            FrameIndex = frame.FrameIndex,
            FrameWidth = frame.Width,
            FrameHeight = frame.Height,
            PtsMs = frame.TimestampMs,
            FrameUtcMs = frameUtcMs,
            Features = features,
            Detections = batch.Detections
        };

        return true;
    }
}

public sealed class BasicDistanceAnalysisStrategy : IAnalysisStrategy
{
    public AnalysisResult Analyze(FsmFrameMetrics current, IReadOnlyList<FsmFrameMetrics> window, AnalysisContext context)
    {
        var features = current.Features;
        var valid0 = features.DistId0ToId2Q1000 < FsmFeatureCalculator.MissingValue;
        var valid1 = features.DistId1ToId2Q1000 < FsmFeatureCalculator.MissingValue;
        var near = valid0 && valid1
            && features.DistId0ToId2Q1000 <= context.Config.NearThresholdQ1000
            && features.DistId1ToId2Q1000 <= context.Config.NearThresholdQ1000;

        int? step = null;
        if (near)
        {
            step = ResolveStep(context);
            context.State.ActiveStep = step;
            context.State.HoldCounter = Math.Max(0, context.Config.HoldFrames);
        }
        else if (context.State.ActiveStep.HasValue && context.State.HoldCounter > 0)
        {
            context.State.HoldCounter--;
            step = context.State.ActiveStep;
        }
        else
        {
            context.State.ActiveStep = null;
        }

        return new AnalysisResult
        {
            Step = step,
            FrameIndex = current.FrameIndex,
            PtsMs = current.PtsMs,
            FrameUtcMs = current.FrameUtcMs,
            DebugNote = near ? "near" : "far"
        };
    }

    private static int? ResolveStep(AnalysisContext context)
    {
        if (context.Config.NearStep.HasValue)
        {
            return context.Config.NearStep.Value;
        }

        if (context.Steps.Count > 0)
        {
            return context.Steps[0].Step;
        }

        return null;
    }
}

public sealed class AnalysisEngine : ILegacyDetectionResultSink
{
    private readonly AnalysisConfig _config;
    private readonly IFrameMetricsExtractor _extractor;
    private readonly IAnalysisStrategy _strategy;
    private readonly RingBuffer<FsmFrameMetrics> _frameWindow;
    private readonly RingBuffer<AnalysisResult> _stateHistory;
    private readonly IModbusHoldingRegisterAccessor _modbusRegisters;
    private readonly AnalysisState _state = new();
    private IReadOnlyList<FsmStepDefinition> _steps = Array.Empty<FsmStepDefinition>();
    private readonly Dictionary<int, int> _stepIndex = new();
    private string _currentRunUuid = string.Empty;
    private readonly object _syncLock = new();

    public AnalysisEngine(
        AnalysisConfig config,
        IFrameMetricsExtractor? extractor = null,
        IAnalysisStrategy? strategy = null,
        IModbusHoldingRegisterAccessor? modbusRegisters = null)
    {
        _config = config;
        _extractor = extractor ?? new FsmFrameMetricsExtractor();
        _strategy = strategy ?? AnalysisStrategyFactory.Create(config);
        _modbusRegisters = modbusRegisters ?? NullModbusHoldingRegisterAccessor.Instance;
        _frameWindow = new RingBuffer<FsmFrameMetrics>(Math.Max(1, config.FrameWindowSize));
        _stateHistory = new RingBuffer<AnalysisResult>(Math.Max(1, config.StateWindowSize));
    }

    public event Action<AnalysisResult>? ResultReady;

    public void UpdateFsmDefinitions(IReadOnlyList<FsmStepDefinition> steps)
    {
        lock (_syncLock)
        {
            var ordered = steps == null
                ? new List<FsmStepDefinition>()
                : new List<FsmStepDefinition>(steps);

            ordered.Sort((a, b) => a.Step.CompareTo(b.Step));
            _steps = ordered;
            _stepIndex.Clear();
            for (var i = 0; i < ordered.Count; i++)
            {
                _stepIndex[ordered[i].Step] = i;
            }
        }

        XTrace.WriteLine("[AnalysisEngine] FSM definitions updated: {0} steps", _steps?.Count ?? 0);
    }

    public bool TryEnqueue(FrameDetections batch)
    {
        if (!_extractor.TryExtract(batch, out var metrics))
        {
            return true;
        }

        lock (_syncLock)
        {
            if (!string.Equals(_currentRunUuid, metrics.RunUuid, StringComparison.OrdinalIgnoreCase))
            {
                XTrace.WriteLine("[AnalysisEngine] New run detected: {0}, resetting analysis state.", metrics.RunUuid);
                Reset(metrics.RunUuid);
            }

            _frameWindow.Add(metrics);
            var previousStep = _state.ActiveStep;
            var context = new AnalysisContext(_config, _steps, _stateHistory, _state, _modbusRegisters);
            var raw = _strategy.Analyze(metrics, _frameWindow, context);
            var result = ApplyTransition(metrics, raw, previousStep);
            _stateHistory.Add(result);

            ResultReady?.Invoke(result);
        }

        return true;
    }

    private void Reset(string runUuid)
    {
        _currentRunUuid = runUuid ?? string.Empty;
        _frameWindow.Clear();
        _stateHistory.Clear();
        ResetState();
        XTrace.WriteLine("[AnalysisEngine] Reset for run {0}", _currentRunUuid);
    }

    public void ResetAnalysis()
    {
        lock (_syncLock)
        {
            _frameWindow.Clear();
            _stateHistory.Clear();
            ResetState();
        }

        XTrace.WriteLine("[AnalysisEngine] Manual reset.");
    }

    private void ResetState()
    {
        _state.ActiveStep = null;
        _state.HoldCounter = 0;
        _state.IsFaulted = false;
        _state.FaultExpectedStateCode = null;
        _state.FaultCurrentStateCode = null;
        _state.FaultNgReason = null;
        _state.Sop1.ResetCycle();
        _state.Sop2.ResetCycle();
        _state.ModbusTriggerStates.Clear();
    }

    private AnalysisResult ApplyTransition(FsmFrameMetrics current, AnalysisResult raw, int? previousStep)
    {
        var newStep = raw.Step;
        var isNg = !string.IsNullOrWhiteSpace(raw.NgReason);
        var isTransition = !isNg && newStep.HasValue && (!previousStep.HasValue || previousStep.Value != newStep.Value);
        var firstStep = _steps.Count > 0 ? _steps[0].Step : (int?)null;
        var isReset = raw.IsReset;
        bool? transitionOk = null;

        if (isTransition)
        {
            if (firstStep.HasValue && newStep == firstStep)
            {
                transitionOk = true;
                isReset = true;
            }
            else if (!previousStep.HasValue)
            {
                transitionOk = false;
            }
            else
            {
                transitionOk = IsSequential(previousStep.Value, newStep!.Value);
            }
        }

        return new AnalysisResult
        {
            RunUuid = current.RunUuid,
            RunStartedUtcMs = current.RunStartedUtcMs,
            SourceKey = current.SourceKey,
            TaskId = current.TaskId,
            StrategyName = string.IsNullOrWhiteSpace(raw.StrategyName) ? _config.Strategy : raw.StrategyName,
            Step = raw.Step,
            Label = raw.Label,
            Score = raw.Score,
            FrameIndex = raw.FrameIndex,
            PtsMs = raw.PtsMs,
            FrameUtcMs = raw.FrameUtcMs,
            DebugNote = raw.DebugNote,
            IsTransition = isTransition,
            IsReset = isReset,
            TransitionOk = transitionOk,
            FromStep = previousStep,
            ToStep = newStep,
            CurrentStateCode = raw.CurrentStateCode,
            ExpectedStateCode = raw.ExpectedStateCode,
            NgReason = raw.NgReason
        };
    }

    private bool IsSequential(int fromStep, int toStep)
    {
        if (!_stepIndex.TryGetValue(fromStep, out var fromIndex))
        {
            return false;
        }

        if (!_stepIndex.TryGetValue(toStep, out var toIndex))
        {
            return false;
        }

        return toIndex == fromIndex + 1;
    }
}
