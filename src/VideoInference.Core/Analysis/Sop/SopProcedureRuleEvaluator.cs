using System;
using System.Collections.Generic;
using System.Linq;

namespace VideoInferenceDemo;

internal static class SopProcedureRuleEvaluator
{
    private const int InnerBoxFrames = 2;
    private const int ChargerFrames = 3;
    private const int ChargerWrongFrames = 3;
    private const int FootPadFrames = 1;
    private const int FootPadWrongFrames = 3;
    private const int ProductFrames = 3;
    private const int ProductWrongFrames = 3;
    private const int WarrantyFrames = 3;
    private const int WarrantyMissingFrames = 5;
    private const int OuterBoxStableFrames = 5;
    private const float OuterBoxMinCenterXRatio = 0.25f;
    private const float OuterBoxMaxCenterXRatio = 0.75f;
    private const float OuterBoxStableToleranceRatio = 0.1f;
    private const float WarrantyStepJitterThresholdPx = 80f;

    public static bool TryEvaluate(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> window,
        AnalysisContext context,
        IReadOnlyList<FsmStepDefinition> orderedSteps,
        out SopProcedureRuleEvaluation evaluation)
    {
        evaluation = null!;
        if (orderedSteps == null)
        {
            return false;
        }

        var allConfiguredSteps = orderedSteps
            .Select((step, index) => new ConfiguredStep(step, ResolveStepKind(step, index)))
            .ToArray();

        var configuredSteps = allConfiguredSteps
            .Where(item => item.Kind != SopProcedureStepKind.Unknown)
            .ToArray();

        if (configuredSteps.Length < 5)
        {
            return false;
        }

        if (context.State.IsFaulted)
        {
            evaluation = new SopProcedureRuleEvaluation(
                context.State.ActiveStep,
                context.State.FaultExpectedStateCode,
                context.State.FaultCurrentStateCode,
                null,
                "procedure_fault_locked",
                context.State.FaultNgReason);
            return true;
        }

        var frames = SopProcedureHelpers.OrderFrames(window, current.PtsMs);
        if (frames.Count == 0)
        {
            frames = new[] { current };
        }

        var activeIndex = context.State.ActiveStep.HasValue
            ? FindStepIndex(configuredSteps, context.State.ActiveStep.Value)
            : -1;
        var expectedIndex = activeIndex < 0 ? 0 : Math.Min(activeIndex + 1, configuredSteps.Length - 1);
        var expected = configuredSteps[expectedIndex];
        var currentStep = activeIndex >= 0 ? configuredSteps[activeIndex] : null;

        if (TryMatchPositive(expected, frames, out var positive))
        {
            var isSopCycleReset = expectedIndex == configuredSteps.Length - 1;
            var debugNote = isSopCycleReset
                ? $"procedure_match; step={expected.Kind}; state={positive.StateCode}; cycle_complete"
                : $"procedure_match; step={expected.Kind}; state={positive.StateCode}";
            
            context.State.ActiveStep = expected.Step.Step;
            context.State.HoldCounter = Math.Max(0, context.Config.HoldFrames);

            if (isSopCycleReset)
            {
                ResetCycleState(context.State);
            }
            
            evaluation = new SopProcedureRuleEvaluation(
                expected.Step.Step,
                positive.StateCode,
                positive.StateCode,
                positive.Score,
                debugNote,
                null,
                isSopCycleReset);
            return true;
        }

        if (TryMatchFault(expected, frames, out var ngReason, out var currentStateCode))
        {
            var stepValue = currentStep?.Step.Step;
            if (stepValue.HasValue)
            {
                context.State.ActiveStep = stepValue.Value;
            }

            context.State.HoldCounter = 0;
            context.State.IsFaulted = true;
            context.State.FaultExpectedStateCode = GetStateCode(expected.Kind);
            context.State.FaultCurrentStateCode = currentStateCode;
            context.State.FaultNgReason = ngReason;
            evaluation = new SopProcedureRuleEvaluation(
                stepValue,
                GetStateCode(expected.Kind),
                currentStateCode,
                null,
                $"procedure_fault; expected={expected.Kind}; reason={ngReason}",
                ngReason);
            return true;
        }

        if (currentStep != null)
        {
            evaluation = new SopProcedureRuleEvaluation(
                currentStep.Step.Step,
                GetStateCode(expected.Kind),
                null,
                null,
                $"procedure_wait; expected={expected.Kind}",
                null);
            return true;
        }

        evaluation = new SopProcedureRuleEvaluation(
            null,
            GetStateCode(expected.Kind),
            null,
            null,
            $"procedure_wait_first; expected={expected.Kind}",
            null);
        return true;
    }

    private static void ResetCycleState(AnalysisState state)
    {
        state.ActiveStep = null;
        state.HoldCounter = 0;
        state.IsFaulted = false;
        state.FaultExpectedStateCode = null;
        state.FaultCurrentStateCode = null;
        state.FaultNgReason = null;
        state.IsSopCyclePendingReset = false;
    }

    private static int FindStepIndex(IReadOnlyList<ConfiguredStep> steps, int step)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].Step.Step == step)
            {
                return i;
            }
        }

        return -1;
    }

    private static SopProcedureStepKind ResolveStepKind(FsmStepDefinition step, int index)
    {
        var normalized = SopRuleAnalysisStrategy.NormalizeStateCode(step.Name ?? string.Empty);

        if (normalized.Contains("适配器", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("充电器", StringComparison.OrdinalIgnoreCase))
        {
            return SopProcedureStepKind.Adapter;
        }

        if (normalized.Contains("脚垫", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("圆片", StringComparison.OrdinalIgnoreCase))
        {
            return SopProcedureStepKind.FootPad;
        }

        if (normalized.Contains("保修卡", StringComparison.OrdinalIgnoreCase))
        {
            return SopProcedureStepKind.WarrantyCard;
        }

        if (normalized.Contains("产品", StringComparison.OrdinalIgnoreCase))
        {
            return SopProcedureStepKind.Product;
        }

        if (normalized.Contains("内盒", StringComparison.OrdinalIgnoreCase))
        {
            return SopProcedureStepKind.InnerBox;
        }

        if (normalized.Equals("完成", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("sop完成", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("流程完成", StringComparison.OrdinalIgnoreCase))
        {
            return SopProcedureStepKind.Complete;
        }

        return index switch
        {
            0 => SopProcedureStepKind.InnerBox,
            1 => SopProcedureStepKind.Adapter,
            2 => SopProcedureStepKind.FootPad,
            3 => SopProcedureStepKind.Product,
            4 => SopProcedureStepKind.WarrantyCard,
            5 => SopProcedureStepKind.Complete,
            _ => SopProcedureStepKind.Unknown
        };
    }

    private static string GetStateCode(SopProcedureStepKind kind)
    {
        return kind switch
        {
            SopProcedureStepKind.InnerBox => "inner_box_ready",
            SopProcedureStepKind.Adapter => "charger_loaded",
            SopProcedureStepKind.FootPad => "foot_pad_loaded",
            SopProcedureStepKind.Product => "product_loaded",
            SopProcedureStepKind.WarrantyCard => "warranty_card_loaded",
            SopProcedureStepKind.Complete => "sop_complete",
            _ => string.Empty
        };
    }

    private static bool TryMatchPositive(
        ConfiguredStep step,
        IReadOnlyList<FsmFrameMetrics> frames,
        out MatchedSignal signal)
    {
        switch (step.Kind)
        {
            case SopProcedureStepKind.InnerBox:
                if (SopProcedureHelpers.HasConsecutiveFrames(
                        frames,
                        InnerBoxFrames,
                        frame => SopProcedureHelpers.TryGetOnlyDetection(frame, SopProcedureHelpers.InnerBox, out _)))
                {
                    signal = new MatchedSignal(
                        GetStateCode(step.Kind),
                        SopProcedureHelpers.AverageScore(
                            frames,
                            InnerBoxFrames,
                            SopProcedureHelpers.InnerBox,
                            frame => SopProcedureHelpers.TryGetOnlyDetection(frame, SopProcedureHelpers.InnerBox, out _)));
                    return true;
                }

                break;

            case SopProcedureStepKind.Adapter:
                if (SopProcedureHelpers.HasConsecutiveFrames(
                        frames,
                        ChargerFrames,
                        frame => SopProcedureHelpers.TryGetDetection(frame, SopProcedureHelpers.Charger, out _)))
                {
                    signal = new MatchedSignal(
                        GetStateCode(step.Kind),
                        SopProcedureHelpers.AverageScore(
                            frames,
                            ChargerFrames,
                            SopProcedureHelpers.Charger,
                            frame => SopProcedureHelpers.TryGetDetection(frame, SopProcedureHelpers.Charger, out _)));
                    return true;
                }

                break;

            case SopProcedureStepKind.FootPad:
                if (SopProcedureHelpers.HasConsecutiveFrames(
                        frames,
                        FootPadFrames,
                        frame => SopProcedureHelpers.TryGetDetection(frame, SopProcedureHelpers.FootPad, out _)))
                {
                    signal = new MatchedSignal(
                        GetStateCode(step.Kind),
                        SopProcedureHelpers.AverageScore(
                            frames,
                            FootPadFrames,
                            SopProcedureHelpers.FootPad,
                            frame => SopProcedureHelpers.TryGetDetection(frame, SopProcedureHelpers.FootPad, out _)));
                    return true;
                }

                break;

            case SopProcedureStepKind.Product:
                if (SopProcedureHelpers.HasConsecutiveFrames(
                        frames,
                        ProductFrames,
                        frame => SopProcedureHelpers.TryGetDetection(frame, SopProcedureHelpers.Product, out _)))
                {
                    signal = new MatchedSignal(
                        GetStateCode(step.Kind),
                        SopProcedureHelpers.AverageScore(
                            frames,
                            ProductFrames,
                            SopProcedureHelpers.Product,
                            frame => SopProcedureHelpers.TryGetDetection(frame, SopProcedureHelpers.Product, out _)));
                    return true;
                }

                break;

            case SopProcedureStepKind.WarrantyCard:
                if (SopProcedureHelpers.HasConsecutiveFrames(
                        frames,
                        WarrantyFrames,
                        frame => SopProcedureHelpers.TryGetDetection(frame, SopProcedureHelpers.WarrantyCard, out _)))
                {
                    signal = new MatchedSignal(
                        GetStateCode(step.Kind),
                        SopProcedureHelpers.AverageScore(
                            frames,
                            WarrantyFrames,
                            SopProcedureHelpers.WarrantyCard,
                            frame => SopProcedureHelpers.TryGetDetection(frame, SopProcedureHelpers.WarrantyCard, out _)));
                    return true;
                }

                break;

            case SopProcedureStepKind.Complete:
                if (SopProcedureHelpers.HasConsecutiveFrames(
                        frames,
                        InnerBoxFrames,
                        frame => true))
                {
                    signal = new MatchedSignal(GetStateCode(step.Kind), null);
                    return true;
                }

                break;
        }

        signal = null!;
        return false;
    }

    private static bool TryMatchFault(
        ConfiguredStep step,
        IReadOnlyList<FsmFrameMetrics> frames,
        out string ngReason,
        out string currentStateCode)
    {
        // 禁用所有错误检测
        ngReason = string.Empty;
        currentStateCode = string.Empty;
        return false;
    }

    private static bool TryMatchRelativeRegion(
        FsmFrameMetrics frame,
        SopProcedureHelpers.DetectionSpec containerSpec,
        SopProcedureHelpers.DetectionSpec targetSpec,
        float minXRatio,
        float maxXRatio,
        float minYRatio,
        float maxYRatio)
    {
        return SopProcedureHelpers.TryGetDetection(frame, containerSpec, out var container) &&
               SopProcedureHelpers.TryGetDetection(frame, targetSpec, out var target) &&
               SopProcedureHelpers.IsDetectionInRelativeRegion(
                   container,
                   target,
                   minXRatio,
                   maxXRatio,
                   minYRatio,
                   maxYRatio);
    }

    private static bool TryMatchInside(
        FsmFrameMetrics frame,
        SopProcedureHelpers.DetectionSpec containerSpec,
        SopProcedureHelpers.DetectionSpec targetSpec)
    {
        return SopProcedureHelpers.TryGetDetection(frame, containerSpec, out var container) &&
               SopProcedureHelpers.TryGetDetection(frame, targetSpec, out var target) &&
               SopProcedureHelpers.IsDetectionInside(container, target, marginPx: 12f);
    }

    private sealed record ConfiguredStep(FsmStepDefinition Step, SopProcedureStepKind Kind);

    private sealed record MatchedSignal(string StateCode, double? Score);
}

internal sealed record SopProcedureRuleEvaluation(
    int? Step,
    string? ExpectedStateCode,
    string? CurrentStateCode,
    double? Score,
    string DebugNote,
    string? NgReason,
    bool IsSopCycleReset = false);

internal enum SopProcedureStepKind
{
    Unknown = 0,
    InnerBox = 1,
    Adapter = 2,
    FootPad = 3,
    Product = 4,
    WarrantyCard = 5,
    Complete = 6
}
