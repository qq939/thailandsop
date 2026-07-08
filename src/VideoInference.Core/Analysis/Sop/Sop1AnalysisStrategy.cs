using System;
using System.Collections.Generic;
using System.Linq;

namespace VideoInferenceDemo;

public sealed class Sop1AnalysisStrategy : IAnalysisStrategy
{
    private const int StepInspectPosition = 1;
    private const int StepVisionInspection = 2;
    private const int StepLubrication = 3;
    private const int StepTightenNylonTube = 4;
    private const int StepAutoFlaringInsertion = 5;
    private const string ModbusKey = "sop1_visual_inspection";

    private static readonly SopModbusTriggerDefinition VisualInspectionTrigger = new()
    {
        TriggerAddress = 10,
        TriggerValue = 1,
        DoneAddress = 11,
        DoneValue = 1,
        ResultAddress = 12,
        OkValue = 1,
        NgValue = 2,
        ErrorValue = 4,
        TimeoutMs = 10000
    };

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
                "sop1_fault_locked",
                context.State.FaultNgReason);
        }

        var frames = OrderFrames(window, current, context.Config);
        var minScore = GetMinScore(context.Config);
        var completedStep = context.State.ActiveStep;
        var nextStep = ResolveNextStep(completedStep);

        if (completedStep >= StepAutoFlaringInsertion)
        {
            ResetCycle(context);
            return CycleReset(current);
        }

        return nextStep switch
        {
            StepInspectPosition => EvaluateInspectPosition(current, frames, context, minScore),
            StepVisionInspection => EvaluateVisionInspection(current, context),
            StepLubrication => EvaluateLubrication(current, frames, context, minScore),
            StepTightenNylonTube => EvaluateTightenNylonTube(current, frames, context, minScore),
            StepAutoFlaringInsertion => EvaluateAutoFlaringInsertion(current, frames, context, minScore),
            _ => Result(current, context, completedStep, null, null, null, "sop1_wait")
        };
    }

    private static AnalysisResult EvaluateInspectPosition(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> frames,
        AnalysisContext context,
        float minScore)
    {
        if (HasFrames(frames, 5, frame => HasDetection(frame, Sop1Class.Product, minScore)))
        {
            return Fault(
                current,
                context,
                context.State.ActiveStep,
                "sop1:inspection_connector_ready",
                "ng:sop1_product_before_inspection",
                "SOP1 step 1 detected product before inspection connector.");
        }

        if (HasFrames(frames, 3, frame => HasDetection(frame, Sop1Class.InspectionConnector, minScore)))
        {
            return CompleteStep(current, context, StepInspectPosition, "sop1_step1_inspection_connector_3_frames");
        }

        return Waiting(current, context, "sop1_wait_step1", "sop1:inspection_connector_ready");
    }

    private static AnalysisResult EvaluateVisionInspection(
        FsmFrameMetrics current,
        AnalysisContext context)
    {
        var evaluation = SopModbusTriggerHelper.Evaluate(
            context,
            current,
            ModbusKey,
            VisualInspectionTrigger);

        if (evaluation.State == SopModbusTriggerState.Ok)
        {
            SopModbusTriggerHelper.Reset(context, ModbusKey);
            return CompleteStep(current, context, StepVisionInspection, evaluation.DebugNote, "sop1:visual_inspection_ok");
        }

        if (evaluation.State == SopModbusTriggerState.Ng ||
            evaluation.State == SopModbusTriggerState.Timeout)
        {
            return Fault(
                current,
                context,
                context.State.ActiveStep,
                "sop1:visual_inspection_ok",
                evaluation.State == SopModbusTriggerState.Timeout
                    ? "ng:sop1_visual_inspection_timeout"
                    : "ng:sop1_visual_inspection",
                evaluation.NgReason ?? "SOP1 visual inspection returned NG.");
        }

        return Waiting(current, context, evaluation.DebugNote, "sop1:visual_inspection_ok");
    }

    private static AnalysisResult EvaluateLubrication(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> frames,
        AnalysisContext context,
        float minScore)
    {
        if (HasFrames(frames, 3, frame => HasDetection(frame, Sop1Class.Product, minScore)))
        {
            return Fault(
                current,
                context,
                context.State.ActiveStep,
                "sop1:lubrication_ready",
                "ng:sop1_product_before_lubrication",
                "SOP1 step 3 detected product before lubrication.");
        }

        if (HasFrames(frames, 3, frame => HasDetection(frame, Sop1Class.Lubrication, minScore)))
        {
            return CompleteStep(current, context, StepLubrication, "sop1_step3_lubrication_3_frames");
        }

        return Waiting(current, context, "sop1_wait_step3", "sop1:lubrication_ready");
    }

    private static AnalysisResult EvaluateTightenNylonTube(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> frames,
        AnalysisContext context,
        float minScore)
    {
        if (HasFrames(frames, 3, frame => HasDetection(frame, Sop1Class.Product, minScore)))
        {
            return CompleteStep(current, context, StepTightenNylonTube, "sop1_step4_product_3_frames");
        }

        return Waiting(current, context, "sop1_wait_step4", "sop1:product_ready");
    }

    private static AnalysisResult EvaluateAutoFlaringInsertion(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> frames,
        AnalysisContext context,
        float minScore)
    {
        var state = context.State.Sop1;
        if (!state.ProductAndPressSeen &&
            HasFrames(frames, 3, frame =>
                HasDetection(frame, Sop1Class.Product, minScore) &&
                HasDetection(frame, Sop1Class.Press, minScore)))
        {
            state.ProductAndPressSeen = true;
            return Waiting(current, context, "sop1_step5_product_and_press_seen_wait_product_disappear", "sop1:product_disappeared");
        }

        if (state.ProductAndPressSeen &&
            HasTailFrames(frames, 5, frame => !HasDetection(frame, Sop1Class.Product, minScore)))
        {
            return CompleteStep(current, context, StepAutoFlaringInsertion, "sop1_step5_product_disappeared_5_frames");
        }

        return Waiting(
            current,
            context,
            state.ProductAndPressSeen ? "sop1_wait_step5_product_disappear" : "sop1_wait_step5_product_and_press",
            state.ProductAndPressSeen ? "sop1:product_disappeared" : "sop1:product_with_press");
    }

    private static AnalysisResult CompleteStep(
        FsmFrameMetrics current,
        AnalysisContext context,
        int step,
        string debugNote,
        string? stateCode = null)
    {
        if (step == StepInspectPosition)
        {
            context.State.Sop1.ResetCycle();
            SopModbusTriggerHelper.Reset(context, ModbusKey);
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

    private static AnalysisResult Fault(
        FsmFrameMetrics current,
        AnalysisContext context,
        int? step,
        string expectedStateCode,
        string currentStateCode,
        string ngReason)
    {
        context.State.IsFaulted = true;
        context.State.FaultExpectedStateCode = expectedStateCode;
        context.State.FaultCurrentStateCode = currentStateCode;
        context.State.FaultNgReason = ngReason;
        context.State.HoldCounter = 0;

        return Result(
            current,
            context,
            step,
            expectedStateCode,
            currentStateCode,
            null,
            $"sop1_fault; expected={expectedStateCode}; current={currentStateCode}",
            ngReason);
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
            StrategyName = AnalysisStrategyNames.Sop1,
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

    private static AnalysisResult CycleReset(FsmFrameMetrics current)
    {
        return new AnalysisResult
        {
            StrategyName = AnalysisStrategyNames.Sop1,
            Step = null,
            FrameIndex = current.FrameIndex,
            PtsMs = current.PtsMs,
            FrameUtcMs = current.FrameUtcMs,
            DebugNote = "sop1_cycle_reset",
            ExpectedStateCode = "sop1:inspection_connector_ready",
            IsReset = true,
            IsSopCycleReset = true
        };
    }

    private static void ResetCycle(AnalysisContext context)
    {
        context.State.ActiveStep = null;
        context.State.HoldCounter = 0;
        context.State.Sop1.ResetCycle();
        SopModbusTriggerHelper.Reset(context, ModbusKey);
    }

    private static int ResolveNextStep(int? completedStep)
    {
        if (!completedStep.HasValue)
        {
            return StepInspectPosition;
        }

        return completedStep.Value >= StepAutoFlaringInsertion
            ? StepInspectPosition
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

    private static bool HasFrames(
        IReadOnlyList<FsmFrameMetrics> frames,
        int requiredFrames,
        Func<FsmFrameMetrics, bool> predicate)
    {
        if (requiredFrames <= 0 || frames.Count < requiredFrames)
        {
            return false;
        }

        return frames.Count(predicate) >= requiredFrames;
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

    private static bool HasDetection(FsmFrameMetrics frame, Sop1Class cls, float minScore)
    {
        return (frame.Detections ?? Array.Empty<DetectionEntity>())
            .Any(item => item != null && item.Score >= minScore && Matches(item, cls));
    }

    private static float GetMinScore(AnalysisConfig config)
    {
        return Math.Clamp(config.SopMinScoreQ1000, 0, 1000) / 1000f;
    }

    private static bool Matches(DetectionEntity detection, Sop1Class cls)
    {
        if (detection.ClassId == GetClassId(cls))
        {
            return true;
        }

        var className = SopRuleAnalysisStrategy.NormalizeStateCode(detection.ClassName ?? string.Empty);
        return cls switch
        {
            Sop1Class.Product => className == "\u4EA7\u54C1" || className == "product",
            Sop1Class.Press => className == "\u538B\u7D27" || className == "press",
            Sop1Class.InspectionConnector => className == "\u68C0\u6D4B\u63A5\u5934" ||
                                             className == "inspection_connector" ||
                                             className == "visual_inspection",
            Sop1Class.Lubrication => className == "\u6DA6\u6ED1" || className == "lubrication",
            _ => false
        };
    }

    private static int GetClassId(Sop1Class cls)
    {
        return cls switch
        {
            Sop1Class.Product => 0,
            Sop1Class.Press => 1,
            Sop1Class.InspectionConnector => 2,
            Sop1Class.Lubrication => 3,
            _ => -1
        };
    }

    private static string GetStateCode(int step)
    {
        return step switch
        {
            StepInspectPosition => "sop1:inspection_connector_ready",
            StepVisionInspection => "sop1:visual_inspection_ok",
            StepLubrication => "sop1:lubrication_ready",
            StepTightenNylonTube => "sop1:product_ready",
            StepAutoFlaringInsertion => "sop1:auto_flaring_inserted",
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

    private enum Sop1Class
    {
        Product,
        Press,
        InspectionConnector,
        Lubrication
    }
}
