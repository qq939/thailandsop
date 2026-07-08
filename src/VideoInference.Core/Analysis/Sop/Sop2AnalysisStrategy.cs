using System;
using System.Collections.Generic;
using System.Linq;

namespace VideoInferenceDemo;

public sealed class Sop2AnalysisStrategy : IAnalysisStrategy
{
    private const int StepTightenNylonTube = 1;
    private const int StepInspectPosition = 2;
    private const int StepVisionInspection = 3;
    private const int StepExpansion = 4;
    private const int StepLubrication = 5;
    private const int StepPressing = 6;
    private const string ModbusKey = "sop2_visual_inspection";
    private const float ReturnToleranceRatio = 0.25f;

    private static readonly SopModbusTriggerDefinition VisualInspectionTrigger = new()
    {
        TriggerAddress = 0,
        TriggerValue = 1,
        DoneAddress = 1,
        DoneValue = 1,
        ResultAddress = 2,
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
                "sop2_fault_locked",
                context.State.FaultNgReason);
        }

        var frames = OrderFrames(window, current, context.Config);
        var minScore = GetMinScore(context.Config);
        var completedStep = context.State.ActiveStep;
        var nextStep = ResolveNextStep(completedStep);

        if (completedStep >= StepPressing)
        {
            ResetCycle(context);
            return CycleReset(current, context);
        }

        return nextStep switch
        {
            StepTightenNylonTube => EvaluateTightenNylonTube(current, frames, context, minScore),
            StepInspectPosition => EvaluateInspectPosition(current, frames, context, minScore),
            StepVisionInspection => EvaluateVisionInspection(current, context),
            StepExpansion => EvaluateExpansion(current, frames, context, minScore),
            StepLubrication => EvaluateLubrication(current, frames, context, minScore),
            StepPressing => EvaluatePressing(current, context, minScore),
            _ => Result(current, context, completedStep, null, null, null, "sop2_wait")
        };
    }

    private static AnalysisResult EvaluateInspectPosition(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> frames,
        AnalysisContext context,
        float minScore)
    {
        if (HasFrames(frames, 3, frame => HasDetection(frame, Sop2Class.InspectionConnector, minScore)))
        {
            return CompleteStep(current, context, StepInspectPosition, "sop2_step2_inspection_connector_3_frames");
        }

        return Waiting(current, context, "sop2_wait_step2", "sop2:inspection_connector_ready");
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
            return CompleteStep(current, context, StepVisionInspection, evaluation.DebugNote, "sop2:visual_inspection_ok");
        }

        if (evaluation.State == SopModbusTriggerState.Ng ||
            evaluation.State == SopModbusTriggerState.Timeout)
        {
            return Fault(
                current,
                context,
                context.State.ActiveStep,
                "sop2:visual_inspection_ok",
                evaluation.State == SopModbusTriggerState.Timeout
                    ? "ng:sop2_visual_inspection_timeout"
                    : "ng:sop2_visual_inspection",
                evaluation.NgReason ?? "SOP2 visual inspection returned NG.");
        }

        return Waiting(current, context, evaluation.DebugNote, "sop2:visual_inspection_ok");
    }

    private static AnalysisResult EvaluateTightenNylonTube(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> frames,
        AnalysisContext context,
        float minScore)
    {
        var productReady = HasFrames(frames, 3, frame =>
            HasDetection(frame, Sop2Class.Product, minScore) || HasDetection(frame, Sop2Class.Product2, minScore));
        var handleReady = HasFrames(frames, 3, frame => HasDetection(frame, Sop2Class.Handle, minScore));

        if (productReady && handleReady)
        {
            return CompleteStep(current, context, StepTightenNylonTube, "sop2_step1_product_and_handle_3_frames");
        }

        return Waiting(current, context, "sop2_wait_step1", "sop2:product_or_product2_with_handle");
    }

    private static AnalysisResult EvaluateExpansion(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> frames,
        AnalysisContext context,
        float minScore)
    {
        if (!context.State.Sop2.ExpansionSeen &&
            HasFrames(frames, 5, frame => HasDetection(frame, Sop2Class.Expansion, minScore)))
        {
            context.State.Sop2.ExpansionSeen = true;
            return Waiting(current, context, "sop2_step4_expansion_seen_wait_disappear", "sop2:expansion_disappeared");
        }

        if (context.State.Sop2.ExpansionSeen &&
            HasTailFrames(frames, 3, frame => !HasDetection(frame, Sop2Class.Expansion, minScore)))
        {
            return CompleteStep(current, context, StepExpansion, "sop2_step4_expansion_disappeared_3_frames");
        }

        return Waiting(
            current,
            context,
            context.State.Sop2.ExpansionSeen ? "sop2_wait_step4_disappear" : "sop2_wait_step4_seen",
            context.State.Sop2.ExpansionSeen ? "sop2:expansion_disappeared" : "sop2:expansion_seen");
    }

    private static AnalysisResult EvaluateLubrication(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> frames,
        AnalysisContext context,
        float minScore)
    {
        if (HasTailFrames(frames, 5, frame =>
                HasDetection(frame, Sop2Class.Lubrication, minScore) &&
                !HasDetection(frame, Sop2Class.InspectionConnector, minScore)))
        {
            return CompleteStep(current, context, StepLubrication, "sop2_step5_lubrication_without_inspection_connector_5_frames");
        }

        return Waiting(current, context, "sop2_wait_step5", "sop2:lubrication_without_inspection_connector");
    }

    private static AnalysisResult EvaluatePressing(
        FsmFrameMetrics current,
        AnalysisContext context,
        float minScore)
    {
        if (!TryGetBestDetection(current, Sop2Class.Press, minScore, out var press) ||
            !TryGetBestDetection(current, Sop2Class.FixedBlock, minScore, out var fixedBlock))
        {
            return Waiting(current, context, "sop2_wait_step6_press_and_fixed_block", "sop2:press_returned");
        }

        var distance = CenterDistance(press, fixedBlock);
        var pressWidth = Math.Max(1f, press.X2 - press.X1);
        var state = context.State.Sop2;
        state.PressBaselineDistance ??= distance;
        var baseline = state.PressBaselineDistance.Value;
        var threshold = pressWidth;
        var returnTolerance = Math.Max(2f, pressWidth * ReturnToleranceRatio);

        if (!state.PressMovedIn && baseline - distance > threshold)
        {
            state.PressMovedIn = true;
            return Waiting(
                current,
                context,
                $"sop2_step6_press_moved_in; baseline={baseline:F1}; distance={distance:F1}; width={pressWidth:F1}",
                "sop2:press_returned");
        }

        if (state.PressMovedIn && distance >= baseline - returnTolerance)
        {
            return CompleteStep(
                current,
                context,
                StepPressing,
                $"sop2_step6_press_returned; baseline={baseline:F1}; distance={distance:F1}; width={pressWidth:F1}");
        }

        return Waiting(
            current,
            context,
            $"sop2_wait_step6; baseline={baseline:F1}; distance={distance:F1}; width={pressWidth:F1}; moved_in={state.PressMovedIn}",
            "sop2:press_returned");
    }

    private static AnalysisResult CompleteStep(
        FsmFrameMetrics current,
        AnalysisContext context,
        int step,
        string debugNote,
        string? stateCode = null)
    {
        if (step == StepTightenNylonTube)
        {
            context.State.Sop2.ResetCycle();
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
            $"sop2_fault; expected={expectedStateCode}; current={currentStateCode}",
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
            StrategyName = AnalysisStrategyNames.Sop2,
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
            StrategyName = AnalysisStrategyNames.Sop2,
            Step = null,
            FrameIndex = current.FrameIndex,
            PtsMs = current.PtsMs,
            FrameUtcMs = current.FrameUtcMs,
            DebugNote = "sop2_cycle_reset",
            ExpectedStateCode = "sop2:product_or_product2_with_handle",
            IsReset = true,
            IsSopCycleReset = true
        };
    }

    private static void ResetCycle(AnalysisContext context)
    {
        context.State.ActiveStep = null;
        context.State.HoldCounter = 0;
        context.State.Sop2.ResetCycle();
        SopModbusTriggerHelper.Reset(context, ModbusKey);
    }

    private static int ResolveNextStep(int? completedStep)
    {
        if (!completedStep.HasValue)
        {
            return StepTightenNylonTube;
        }

        return completedStep.Value >= StepPressing
            ? StepTightenNylonTube
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

    private static bool HasDetection(FsmFrameMetrics frame, Sop2Class cls, float minScore)
    {
        return (frame.Detections ?? Array.Empty<DetectionEntity>())
            .Any(item => item != null && item.Score >= minScore && Matches(item, cls));
    }

    private static bool TryGetBestDetection(
        FsmFrameMetrics frame,
        Sop2Class cls,
        float minScore,
        out DetectionEntity detection)
    {
        detection = (frame.Detections ?? Array.Empty<DetectionEntity>())
            .Where(item => item != null && item.Score >= minScore && Matches(item, cls))
            .OrderByDescending(item => item.Score)
            .FirstOrDefault()!;

        return detection != null;
    }

    private static float GetMinScore(AnalysisConfig config)
    {
        return Math.Clamp(config.SopMinScoreQ1000, 0, 1000) / 1000f;
    }

    private static bool Matches(DetectionEntity detection, Sop2Class cls)
    {
        if (detection.ClassId == GetClassId(cls))
        {
            return true;
        }

        var className = SopRuleAnalysisStrategy.NormalizeStateCode(detection.ClassName ?? string.Empty);
        return cls switch
        {
            Sop2Class.Product => className == "\u4EA7\u54C1" || className == "product",
            Sop2Class.Product2 => className == "\u4EA7\u54C12" || className == "product2" || className == "product_2",
            Sop2Class.Press => className == "\u538B\u7D27" || className == "press",
            Sop2Class.FixedBlock => className == "\u56FA\u5B9A\u5757" || className == "fixed_block" || className == "fixedblock",
            Sop2Class.Expansion => className == "\u6269\u53E3" || className == "expansion" || className == "flaring",
            Sop2Class.Handle => className == "\u628A\u624B" || className == "handle",
            Sop2Class.Connector => className == "\u63A5\u5934" || className == "connector",
            Sop2Class.InspectionConnector => className == "\u68C0\u6D4B\u63A5\u5934" || className == "inspection_connector",
            Sop2Class.Lubrication => className == "\u6DA6\u6ED1" || className == "lubrication",
            _ => false
        };
    }

    private static int GetClassId(Sop2Class cls)
    {
        return cls switch
        {
            Sop2Class.Product => 0,
            Sop2Class.Product2 => 1,
            Sop2Class.Press => 2,
            Sop2Class.FixedBlock => 3,
            Sop2Class.Expansion => 4,
            Sop2Class.Handle => 5,
            Sop2Class.Connector => 6,
            Sop2Class.InspectionConnector => 7,
            Sop2Class.Lubrication => 8,
            _ => -1
        };
    }

    private static float CenterDistance(DetectionEntity a, DetectionEntity b)
    {
        var ax = (a.X1 + a.X2) * 0.5f;
        var ay = (a.Y1 + a.Y2) * 0.5f;
        var bx = (b.X1 + b.X2) * 0.5f;
        var by = (b.Y1 + b.Y2) * 0.5f;
        var dx = ax - bx;
        var dy = ay - by;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    private static string GetStateCode(int step)
    {
        return step switch
        {
            StepTightenNylonTube => "sop2:nylon_tube_tightened",
            StepInspectPosition => "sop2:inspection_connector_ready",
            StepVisionInspection => "sop2:visual_inspection_ok",
            StepExpansion => "sop2:expansion_completed",
            StepLubrication => "sop2:lubrication_completed",
            StepPressing => "sop2:press_completed",
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

    private enum Sop2Class
    {
        Product,
        Product2,
        Press,
        FixedBlock,
        Expansion,
        Handle,
        Connector,
        InspectionConnector,
        Lubrication
    }
}
