using System;
using System.Collections.Generic;
using System.Linq;

namespace VideoInferenceDemo;

public sealed class Sop3AnalysisStrategy : IAnalysisStrategy
{
    private const int StepDetector = 1;
    private const int StepLabels = 2;
    private const int StepClip = 3;
    private const int StepMovingLabel = 4;
    private const int StepDone = 5;

    private const int RequiredDetectorFrames = 3;
    private const int RequiredLabelsFrames = 5;
    private const int RequiredClipFrames = 3;
    private const int RequiredMovingLabelExitedFrames = 3;
    private const int RequiredDoneFrames = 3;

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
                "sop3_fault_locked",
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
            StepDetector => EvaluateDetector(current, frames, context, minScore),
            StepLabels => EvaluateLabels(current, frames, context, minScore),
            StepClip => EvaluateClip(current, frames, context, minScore),
            StepMovingLabel => EvaluateMovingLabel(current, frames, context, minScore),
            StepDone => EvaluateDone(current, frames, context, minScore),
            _ => Result(current, context, completedStep, null, null, null, "sop3_wait")
        };
    }

    private static AnalysisResult EvaluateDetector(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> frames,
        AnalysisContext context,
        float minScore)
    {
        // 第一步不允许出现 Done
        if (frames.Any(frame => HasDetection(frame, Sop3Class.Done, minScore)))
        {
            return Waiting(current, context, "sop3_wait_step1_done_forbidden", "sop3:detector_detected");
        }

        if (HasTailFrames(frames, RequiredDetectorFrames, frame => HasMinDetectors(frame, 3, minScore)))
        {
            return CompleteStep(current, context, StepDetector, "sop3_step1_detector_3_frames");
        }

        return Waiting(current, context, "sop3_wait_step1", "sop3:detector_detected");
    }

    private static bool HasMinDetectors(FsmFrameMetrics frame, int minCount, float minScore)
    {
        return (frame.Detections ?? Array.Empty<DetectionEntity>())
            .Count(item => item != null && item.Score >= minScore && Matches(item, Sop3Class.Detector)) >= minCount;
    }

    private static AnalysisResult EvaluateLabels(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> frames,
        AnalysisContext context,
        float minScore)
    {
        if (HasTailFrames(frames, RequiredLabelsFrames, frame => HasDetection(frame, Sop3Class.Labels, minScore)))
        {
            return CompleteStep(current, context, StepLabels, "sop3_step2_labels_5_frames");
        }

        return Waiting(current, context, "sop3_wait_step2", "sop3:labels_detected");
    }

    private static AnalysisResult EvaluateClip(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> frames,
        AnalysisContext context,
        float minScore)
    {
        if (HasTailFrames(frames, RequiredClipFrames, frame => HasMinClips(frame, 5, minScore)))
        {
            return CompleteStep(current, context, StepClip, "sop3_step3_clip_5_3_frames");
        }

        return Waiting(current, context, "sop3_wait_step3", "sop3:clip_detected");
    }

    private static bool HasMinClips(FsmFrameMetrics frame, int minCount, float minScore)
    {
        return (frame.Detections ?? Array.Empty<DetectionEntity>())
            .Count(item => item != null && item.Score >= minScore && Matches(item, Sop3Class.Clip)) > minCount;
    }

    private static AnalysisResult EvaluateMovingLabel(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> frames,
        AnalysisContext context,
        float minScore)
    {
        var state = context.State.Sop3;

        if (!state.MovingLabelSeen &&
            HasTailFrames(frames, 3, frame => HasDetection(frame, Sop3Class.MovingLabel, minScore)))
        {
            state.MovingLabelSeen = true;
            return Waiting(current, context, "sop3_step4_moving_label_seen", "sop3:moving_label_moving");
        }

        if (state.MovingLabelSeen &&
            HasTailFrames(frames, RequiredMovingLabelExitedFrames, frame => !HasDetection(frame, Sop3Class.MovingLabel, minScore)))
        {
            return CompleteStep(current, context, StepMovingLabel, "sop3_step4_moving_label_exited_3_frames");
        }

        return Waiting(
            current,
            context,
            state.MovingLabelSeen ? "sop3_wait_step4_exiting" : "sop3_wait_step4_seen",
            "sop3:moving_label_exited");
    }

    private static AnalysisResult EvaluateDone(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> frames,
        AnalysisContext context,
        float minScore)
    {
        if (HasTailFrames(frames, RequiredDoneFrames, frame => HasDetection(frame, Sop3Class.Done, minScore)))
        {
            return CompleteStep(current, context, StepDone, "sop3_step5_done_3_frames");
        }

        return Waiting(current, context, "sop3_wait_step5", "sop3:done_detected");
    }

    private static AnalysisResult CompleteStep(
        FsmFrameMetrics current,
        AnalysisContext context,
        int step,
        string debugNote,
        string? stateCode = null)
    {
        if (step == StepDetector)
        {
            context.State.Sop3.ResetCycle();
        }

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
            StrategyName = AnalysisStrategyNames.Sop3,
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
            StrategyName = AnalysisStrategyNames.Sop3,
            Step = null,
            FrameIndex = current.FrameIndex,
            PtsMs = current.PtsMs,
            FrameUtcMs = current.FrameUtcMs,
            DebugNote = "sop3_cycle_reset",
            ExpectedStateCode = "sop3:detector_detected",
            IsReset = true,
            IsSopCycleReset = true
        };
    }

    private static void ResetCycle(AnalysisContext context)
    {
        context.State.ActiveStep = null;
        context.State.HoldCounter = 0;
        context.State.Sop3.ResetCycle();
    }

    private static int ResolveNextStep(int? completedStep)
    {
        if (!completedStep.HasValue)
        {
            return StepDetector;
        }

        return completedStep.Value >= StepDone
            ? StepDetector
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

    private static bool HasDetection(FsmFrameMetrics frame, Sop3Class cls, float minScore)
    {
        return (frame.Detections ?? Array.Empty<DetectionEntity>())
            .Any(item => item != null && item.Score >= minScore && Matches(item, cls));
    }

    private static float GetMinScore(AnalysisConfig config)
    {
        return config.ConfidenceThreshold;
    }

    private static bool Matches(DetectionEntity detection, Sop3Class cls)
    {
        if (detection.ClassId == GetClassId(cls))
        {
            return true;
        }

        var className = SopRuleAnalysisStrategy.NormalizeStateCode(detection.ClassName ?? string.Empty);
        return cls switch
        {
            Sop3Class.QR => className == "qr",
            Sop3Class.Labels => className == "labels",
            Sop3Class.Detector => className == "detector",
            Sop3Class.MovingLabel => className == "movinglabel" || className == "moving_label",
            Sop3Class.Done => className == "done",
            Sop3Class.Clip => className == "clip",
            _ => false
        };
    }

    private static int GetClassId(Sop3Class cls)
    {
        return cls switch
        {
            Sop3Class.QR => 0,
            Sop3Class.Labels => 1,
            Sop3Class.Detector => 2,
            Sop3Class.MovingLabel => 3,
            Sop3Class.Done => 4,
            Sop3Class.Clip => 5,
            _ => -1
        };
    }

    private static string GetStateCode(int step)
    {
        return step switch
        {
            StepDetector => "sop3:detector_detected",
            StepLabels => "sop3:labels_detected",
            StepClip => "sop3:clip_detected",
            StepMovingLabel => "sop3:moved_to_stack",
            StepDone => "sop3:done_detected",
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

    private enum Sop3Class
    {
        QR,
        Labels,
        Detector,
        MovingLabel,
        Done,
        Clip
    }
}
