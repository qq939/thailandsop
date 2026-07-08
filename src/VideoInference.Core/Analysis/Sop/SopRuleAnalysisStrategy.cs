using System;
using System.Collections.Generic;
using System.Linq;

namespace VideoInferenceDemo;

public sealed class SopRuleAnalysisStrategy : IAnalysisStrategy
{
    public AnalysisResult Analyze(
        FsmFrameMetrics current,
        IReadOnlyList<FsmFrameMetrics> window,
        AnalysisContext context)
    {
        if (!IsSourceTaskAllowed(current, context.Config))
        {
            return new AnalysisResult
            {
                Step = context.State.ActiveStep,
                FrameIndex = current.FrameIndex,
                PtsMs = current.PtsMs,
                FrameUtcMs = current.FrameUtcMs,
                DebugNote = $"ignored_task; task={current.TaskId}"
            };
        }

        var windowState = SopWindowStateBuilder.Build(
            current,
            window,
            context.Config.SopWindowMs,
            context.Config.SopMinScoreQ1000);
        var orderedSteps = context.Steps.OrderBy(step => step.Step).ToList();

        if (SopProcedureRuleEvaluator.TryEvaluate(current, window, context, orderedSteps, out var procedureEvaluation))
        {
            return new AnalysisResult
            {
                Step = procedureEvaluation.Step,
                Label = procedureEvaluation.CurrentStateCode,
                Score = procedureEvaluation.Score,
                FrameIndex = current.FrameIndex,
                PtsMs = current.PtsMs,
                FrameUtcMs = current.FrameUtcMs,
                DebugNote = procedureEvaluation.DebugNote,
                CurrentStateCode = procedureEvaluation.CurrentStateCode,
                ExpectedStateCode = procedureEvaluation.ExpectedStateCode,
                NgReason = procedureEvaluation.NgReason
            };
        }

        var ruleContext = new SopRuleContext(current, context, windowState);
        var satisfiedStates = SopSatisfiedStates.FromMatches(SopProjectRules.Match(ruleContext));
        var decision = ResolveStep(current, orderedSteps, satisfiedStates, context.State, context.Config.HoldFrames);

        return new AnalysisResult
        {
            Step = decision.Step,
            Label = decision.CurrentStateCode,
            Score = decision.Score,
            FrameIndex = current.FrameIndex,
            PtsMs = current.PtsMs,
            FrameUtcMs = current.FrameUtcMs,
            DebugNote = decision.DebugNote,
            CurrentStateCode = decision.CurrentStateCode,
            ExpectedStateCode = decision.ExpectedStateCode,
            NgReason = decision.NgReason
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

    private static SopStepDecision ResolveStep(
        FsmFrameMetrics current,
        IReadOnlyList<FsmStepDefinition> orderedSteps,
        SopSatisfiedStates satisfiedStates,
        AnalysisState state,
        int holdFrames)
    {
        if (orderedSteps.Count == 0)
        {
            state.ActiveStep = null;
            return SopStepDecision.None("no_steps");
        }

        var activeIndex = state.ActiveStep.HasValue
            ? FindStepIndex(orderedSteps, state.ActiveStep.Value)
            : -1;

        if (activeIndex < 0)
        {
            var firstSatisfied = FindFirstSatisfiedStep(orderedSteps, 0, satisfiedStates);
            if (firstSatisfied.HasValue)
            {
                state.ActiveStep = firstSatisfied.Value.Step.Step;
                state.HoldCounter = Math.Max(0, holdFrames);
                return SopStepDecision.ForStep(firstSatisfied.Value.Step, firstSatisfied.Value.ExpectedStateCode, firstSatisfied.Value.State, "start");
            }

            state.ActiveStep = null;
            return SopStepDecision.None("waiting_first_step");
        }

        if (activeIndex + 1 < orderedSteps.Count)
        {
            var nextStep = orderedSteps[activeIndex + 1];
            if (TryGetSatisfiedState(nextStep, activeIndex + 1, satisfiedStates, out var nextExpected, out var nextState))
            {
                state.ActiveStep = nextStep.Step;
                state.HoldCounter = Math.Max(0, holdFrames);
                return SopStepDecision.ForStep(nextStep, nextExpected, nextState, "advance_next");
            }
        }

        var currentStep = orderedSteps[activeIndex];
        if (TryGetSatisfiedState(currentStep, activeIndex, satisfiedStates, out var currentExpected, out var currentState))
        {
            state.ActiveStep = currentStep.Step;
            state.HoldCounter = Math.Max(0, holdFrames);
            return SopStepDecision.ForStep(currentStep, currentExpected, currentState, "hold_current");
        }

        var jumpedStep = FindFirstSatisfiedStep(orderedSteps, activeIndex + 2, satisfiedStates);
        if (jumpedStep.HasValue)
        {
            state.ActiveStep = jumpedStep.Value.Step.Step;
            state.HoldCounter = Math.Max(0, holdFrames);
            return SopStepDecision.ForStep(
                jumpedStep.Value.Step,
                jumpedStep.Value.ExpectedStateCode,
                jumpedStep.Value.State,
                "jumped_step",
                $"Expected next step after {currentStep.Step}, but matched step {jumpedStep.Value.Step.Step}.");
        }

        if (state.HoldCounter > 0)
        {
            state.HoldCounter--;
            return SopStepDecision.ForStep(currentStep, ResolveExpectedStateCode(currentStep, activeIndex), null, "hold_counter");
        }

        state.ActiveStep = null;
        return SopStepDecision.None("no_rule_matched");
    }

    private static int FindStepIndex(IReadOnlyList<FsmStepDefinition> orderedSteps, int step)
    {
        for (var i = 0; i < orderedSteps.Count; i++)
        {
            if (orderedSteps[i].Step == step)
            {
                return i;
            }
        }

        return -1;
    }

    private static (FsmStepDefinition Step, string ExpectedStateCode, SopMatchedState State)? FindFirstSatisfiedStep(
        IReadOnlyList<FsmStepDefinition> orderedSteps,
        int startIndex,
        SopSatisfiedStates satisfiedStates)
    {
        for (var i = Math.Max(0, startIndex); i < orderedSteps.Count; i++)
        {
            if (TryGetSatisfiedState(orderedSteps[i], i, satisfiedStates, out var expected, out var state))
            {
                return (orderedSteps[i], expected, state);
            }
        }

        return null;
    }

    private static bool TryGetSatisfiedState(
        FsmStepDefinition step,
        int stepIndex,
        SopSatisfiedStates satisfiedStates,
        out string expectedStateCode,
        out SopMatchedState state)
    {
        expectedStateCode = ResolveExpectedStateCode(step, stepIndex);
        foreach (var candidate in SplitExpectedStateCodes(expectedStateCode))
        {
            if (satisfiedStates.TryGet(candidate, out state!))
            {
                return true;
            }
        }

        state = null!;
        return false;
    }

    private static string ResolveExpectedStateCode(FsmStepDefinition step, int stepIndex)
    {
        if (!string.IsNullOrWhiteSpace(step.ExpectedStateCode))
        {
            return step.ExpectedStateCode.Trim();
        }

        if (TryResolveExpectedStateCodeFromStepName(step.Name, out var inferred))
        {
            return inferred;
        }

        return $"class:{stepIndex}";
    }

    private static bool TryResolveExpectedStateCodeFromStepName(string? stepName, out string expectedStateCode)
    {
        var normalized = NormalizeStepName(stepName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            expectedStateCode = string.Empty;
            return false;
        }

        if (ContainsAll(normalized, "产品", "内盒") || ContainsAll(normalized, "product", "inner", "box"))
        {
            expectedStateCode = "product_in_inner_box";
            return true;
        }

        if (ContainsAll(normalized, "圆片", "内盒") || ContainsAll(normalized, "disk", "inner", "box"))
        {
            expectedStateCode = "disk_in_inner_box";
            return true;
        }

        if (ContainsAll(normalized, "充电器", "外盒") || ContainsAll(normalized, "charger", "outer", "box"))
        {
            expectedStateCode = "charger_in_outer_box";
            return true;
        }

        if (ContainsAll(normalized, "保修卡", "外盒") || ContainsAll(normalized, "warranty", "outer", "box"))
        {
            expectedStateCode = "warranty_card_in_outer_box";
            return true;
        }

        if (ContainsAll(normalized, "内盒", "外盒") || ContainsAll(normalized, "inner", "outer", "box"))
        {
            expectedStateCode = "inner_box_in_outer_box";
            return true;
        }

        if (normalized.Contains("内盒", StringComparison.OrdinalIgnoreCase) || ContainsAll(normalized, "inner", "box"))
        {
            expectedStateCode = "inner_box_visible";
            return true;
        }

        if (normalized.Contains("圆片", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("圆盘", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("disk", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("disc", StringComparison.OrdinalIgnoreCase))
        {
            expectedStateCode = "disk_visible";
            return true;
        }

        if (normalized.Contains("产品", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("product", StringComparison.OrdinalIgnoreCase))
        {
            expectedStateCode = "product_visible";
            return true;
        }

        if (normalized.Contains("充电器", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("charger", StringComparison.OrdinalIgnoreCase))
        {
            expectedStateCode = "charger_visible";
            return true;
        }

        if (normalized.Contains("保修卡", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("warranty", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("card", StringComparison.OrdinalIgnoreCase))
        {
            expectedStateCode = "warranty_card_visible";
            return true;
        }

        if (normalized.Contains("外盒", StringComparison.OrdinalIgnoreCase) || ContainsAll(normalized, "outer", "box"))
        {
            expectedStateCode = "outer_box_visible";
            return true;
        }

        expectedStateCode = string.Empty;
        return false;
    }

    private static string NormalizeStepName(string? stepName)
    {
        return string.IsNullOrWhiteSpace(stepName)
            ? string.Empty
            : stepName.Trim().ToLowerInvariant();
    }

    private static bool ContainsAll(string text, params string[] parts)
    {
        foreach (var part in parts)
        {
            if (!text.Contains(part, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> SplitExpectedStateCodes(string expectedStateCode)
    {
        return expectedStateCode
            .Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeStateCode);
    }

    public static string NormalizeStateCode(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
    }

    private sealed class SopSatisfiedStates
    {
        private readonly IReadOnlyDictionary<string, SopMatchedState> _states;

        public SopSatisfiedStates(IReadOnlyDictionary<string, SopMatchedState> states)
        {
            _states = states;
        }

        public static SopSatisfiedStates FromMatches(IEnumerable<SopMatchedState> matches)
        {
            var states = new Dictionary<string, SopMatchedState>(StringComparer.OrdinalIgnoreCase);
            foreach (var match in matches)
            {
                if (string.IsNullOrWhiteSpace(match.StateCode))
                {
                    continue;
                }

                states[NormalizeStateCode(match.StateCode)] = match;
            }

            return new SopSatisfiedStates(states);
        }

        public bool TryGet(string stateCode, out SopMatchedState state)
        {
            return _states.TryGetValue(NormalizeStateCode(stateCode), out state!);
        }
    }

    private readonly record struct SopStepDecision(
        int? Step,
        string? ExpectedStateCode,
        string? CurrentStateCode,
        double? Score,
        string DebugNote,
        string? NgReason)
    {
        public static SopStepDecision None(string reason) => new(null, null, null, null, reason, null);

        public static SopStepDecision ForStep(
            FsmStepDefinition step,
            string expectedStateCode,
            SopMatchedState? state,
            string reason,
            string? ngReason = null)
        {
            var currentStateCode = state != null ? NormalizeStateCode(state.StateCode) : null;
            var score = state?.Score;
            var note = state == null
                ? $"{reason}; expected={expectedStateCode}"
                : $"{reason}; expected={expectedStateCode}; current={currentStateCode}; score={state.Score:F3}; {state.Note}";
            return new SopStepDecision(step.Step, expectedStateCode, currentStateCode, score, note, ngReason);
        }
    }
}
