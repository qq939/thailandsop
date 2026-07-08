using System;
using System.Collections.Generic;
using System.Linq;

namespace VideoInferenceDemo;

public sealed class Sop4AnalysisStrategy : IAnalysisStrategy
{
    private const int StepQR = 1;
    private const int StepNozzling = 2;
    private const int StepPlugging = 3;
    private const int StepDone = 4;

    private const int RequiredQRFrames = 5;
    private const int RequiredNozzlingFrames = 5;
    private const int RequiredPluggingFrames = 3;
    private const int RequiredNoLabelFrames = 5;
    private const int RequiredDoneFrames = 1;

    public AnalysisResult Analyze(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> window,
        AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsSourceTaskAllowed(current, context.Config))
        {
            return Result(
                current,
                context,
                context.State.ActiveStep,
                null,
                null,
                null,
                $"ignored_task; task={current.TaskId}");
        }

        if (context.State.IsFaulted)
        {
            return Result(
                current,
                context,
                context.State.ActiveStep,
                context.State.FaultExpectedStateCode,
                context.State.FaultCurrentStateCode,
                null,
                "sop4_fault_locked",
                context.State.FaultNgReason);
        }

        var frames = OrderFrames(window, current, context.Config);
        var minScore = GetMinScore(context.Config);
        var completedStep = context.State.ActiveStep;
        var nextStep = ResolveNextStep(completedStep);

        if (completedStep >= StepDone)
        {
            ResetCycle(context);
            return CycleReset(current, context);
        }

        return nextStep switch
        {
            StepQR => EvaluateQR(current, frames, context, minScore),
            StepNozzling => EvaluateNozzling(current, frames, context, minScore),
            StepPlugging => EvaluatePlugging(current, frames, context, minScore),
            StepDone => EvaluateDone(current, frames, context, minScore),
            _ => Result(current, context, completedStep, null, null, null, "sop4_wait")
        };
    }

    private static AnalysisResult EvaluateQR(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> frames,
        AnalysisContext context,
        float minScore)
    {
        if (HasTailFrames(frames, RequiredQRFrames, frame => HasDetection(frame, Sop4Class.QR, minScore)))
        {
            return CompleteStep(current, context, StepQR, "sop4_step1_qr_5_frames");
        }

        return Waiting(current, context, "sop4_wait_step1", "sop4:qr_detected");
    }

    private static AnalysisResult EvaluateNozzling(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> frames,
        AnalysisContext context,
        float minScore)
    {
        if (HasTailFrames(frames, RequiredNozzlingFrames, frame => HasDetection(frame, Sop4Class.Nozzling, minScore)))
        {
            return CompleteStep(current, context, StepNozzling, "sop4_step2_nozzling_5_frames");
        }

        return Waiting(current, context, "sop4_wait_step2", "sop4:nozzling_detected");
    }

    private static AnalysisResult EvaluatePlugging(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> frames,
        AnalysisContext context,
        float minScore)
    {
        if (HasTailFrames(frames, RequiredPluggingFrames, frame => HasDetection(frame, Sop4Class.Plugging, minScore)))
        {
            return CompleteStep(current, context, StepPlugging, "sop4_step3_plugging_3_frames");
        }

        return Waiting(current, context, "sop4_wait_step3", "sop4:plugging_detected");
    }

    private static AnalysisResult EvaluateDone(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> frames,
        AnalysisContext context,
        float minScore)
    {
        if (HasTailFrames(frames, RequiredDoneFrames, frame => HasDetection(frame, Sop4Class.Done, minScore)))
        {
            return CompleteStep(current, context, StepDone, "sop4_step4_done_3_frames");
        }

        return Waiting(current, context, "sop4_wait_step4", "sop4:done_detected");
    }

    private static bool HasAnyDetection(FsmFrameMetrics frame, float minScore)
    {
        return (frame.Detections ?? Array.Empty<DetectionEntity>())
            .Any(item => item != null && item.Score >= minScore);
    }

    private static AnalysisResult CompleteStep(
        FsmFrameMetrics current,
        AnalysisContext context,
        int step,
        string debugNote,
        string? stateCode = null)
    {
        context.State.ActiveStep = step;
        context.State.HoldCounter = Math.Max(0, context.Config.HoldFrames);

        return Result(
            current,
            context,
            step,
            stateCode ?? GetStateCode(step),
            stateCode ?? GetStateCode(step),
            null,
            debugNote);
    }

    private static AnalysisResult Waiting(
        FsmFrameMetrics current,
        AnalysisContext context,
        string debugNote,
        string expectedStateCode)
    {
        return Result(
            current,
            context,
            context.State.ActiveStep,
            expectedStateCode,
            null,
            null,
            debugNote);
    }

    private static AnalysisResult Result(
        FsmFrameMetrics current,
        AnalysisContext context,
        int? step,
        string? expectedStateCode,
        string? currentStateCode,
        double? score,
        string debugNote,
        string? ngReason = null)
    {
        return new AnalysisResult
        {
            StrategyName = AnalysisStrategyNames.Sop4,
            Step = step,
            Label = currentStateCode,
            Score = score,
            FrameIndex = current.FrameIndex,
            PtsMs = current.PtsMs,
            FrameUtcMs = current.FrameUtcMs,
            DebugNote = debugNote,
            CurrentStateCode = currentStateCode,
            ExpectedStateCode = expectedStateCode,
            NgReason = ngReason
        };
    }

    private static AnalysisResult CycleReset(FsmFrameMetrics current, AnalysisContext context)
    {
        return new AnalysisResult
        {
            StrategyName = AnalysisStrategyNames.Sop4,
            Step = null,
            FrameIndex = current.FrameIndex,
            PtsMs = current.PtsMs,
            FrameUtcMs = current.FrameUtcMs,
            DebugNote = "sop4_cycle_reset",
            ExpectedStateCode = "sop4:qr_detected",
            IsReset = true,
            IsSopCycleReset = true
        };
    }

    private static void ResetCycle(AnalysisContext context)
    {
        context.State.ActiveStep = null;
        context.State.HoldCounter = 0;
        context.State.Sop4.ResetCycle();
    }

    private static int ResolveNextStep(int? completedStep)
    {
        if (!completedStep.HasValue)
        {
            return StepQR;
        }

        return completedStep.Value >= StepDone
            ? StepQR
            : completedStep.Value + 1;
    }

    private static IReadOnlyList<FsmFrameMetrics> OrderFrames(
        IReadOnlyList<FsmFrameMetrics> window,
        FsmFrameMetrics current,
        AnalysisConfig config)
    {
        var windowMs = config.SopWindowMs;
        var minPts = current.PtsMs - Math.Max(1, windowMs);
        var frames = (window ?? Array.Empty<FsmFrameMetrics>())
            .Where(frame => frame.PtsMs >= minPts && frame.PtsMs <= current.PtsMs)
            .Where(frame => IsSourceTaskAllowed(frame, config))
            .OrderBy(frame => frame.FrameIndex)
            .ThenBy(frame => frame.PtsMs)
            .ToList();

        if (frames.Count == 0)
        {
            frames.Add(current);
        }

        return frames;
    }

    private static bool HasTailFrames(
        IReadOnlyList<FsmFrameMetrics> frames,
        int requiredFrames,
        Func<FsmFrameMetrics, bool> predicate)
    {
        if (requiredFrames <= 0 || frames.Count < requiredFrames)
        {
            return false;
        }

        var count = 0;
        for (var i = frames.Count - 1; i >= 0; i--)
        {
            if (!predicate(frames[i]))
            {
                break;
            }

            count++;
            if (count >= requiredFrames)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDetection(FsmFrameMetrics frame, Sop4Class cls, float minScore)
    {
        return (frame.Detections ?? Array.Empty<DetectionEntity>())
            .Any(item => item != null && item.Score >= minScore && Matches(item, cls));
    }

    private static float GetMinScore(AnalysisConfig config)
    {
        return config.ConfidenceThreshold;
    }

    private static bool Matches(DetectionEntity detection, Sop4Class cls)
    {
        if (detection.ClassId == GetClassId(cls))
        {
            return true;
        }

        var className = SopRuleAnalysisStrategy.NormalizeStateCode(detection.ClassName ?? string.Empty);
        return cls switch
        {
            Sop4Class.QR => className == "qr",
            Sop4Class.Plugging => className == "plugging",
            Sop4Class.Nozzling => className == "nozzling",
            Sop4Class.MovingLabel => className == "movinglabel" || className == "moving_label",
            Sop4Class.Done => className == "done",
            _ => false
        };
    }

    private static int GetClassId(Sop4Class cls)
    {
        return cls switch
        {
            Sop4Class.QR => 0,
            Sop4Class.Plugging => 1,
            Sop4Class.Nozzling => 2,
            Sop4Class.MovingLabel => 3,
            Sop4Class.Done => 4,
            _ => -1
        };
    }

    private static string GetStateCode(int step)
    {
        return step switch
        {
            StepQR => "sop4:qr_detected",
            StepNozzling => "sop4:nozzling_detected",
            StepPlugging => "sop4:plugging_detected",
            StepDone => "sop4:done_detected",
            _ => string.Empty
        };
    }

    private static bool IsSourceTaskAllowed(FsmFrameMetrics current, AnalysisConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.SourceTaskId))
        {
            return true;
        }

        return string.Equals(current.TaskId, config.SourceTaskId.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private enum Sop4Class
    {
        QR,
        Plugging,
        Nozzling,
        MovingLabel,
        Done
    }
}
