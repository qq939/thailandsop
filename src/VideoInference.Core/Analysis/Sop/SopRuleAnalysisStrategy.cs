// 引入 System 命名空间，包含基本类型和工具函数
using System;
// 引入 System.Collections.Generic 命名空间，包含泛型集合
using System.Collections.Generic;
// 引入 System.Linq 命名空间，包含 LINQ 查询操作
using System.Linq;

// 定义命名空间
namespace VideoInferenceDemo;

// 定义密封类，实现 SOP 规则分析策略
// sealed 表示类不能被继承
// IAnalysisStrategy 是接口，表示分析策略
public sealed class SopRuleAnalysisStrategy : IAnalysisStrategy
{
    // 分析当前帧，返回分析结果
    public AnalysisResult Analyze(
        FsmFrameMetrics current,              // 当前帧的度量数据
        IReadOnlyList<FsmFrameMetrics> window,  // 帧窗口的只读列表
        AnalysisContext context)               // 分析上下文
    {
        // 检查源任务是否允许
        if (!IsSourceTaskAllowed(current, context.Config))
        {
            // 任务不允许，返回忽略结果
            return new AnalysisResult
            {
                Step = context.State.ActiveStep,  // 当前步骤不变
                FrameIndex = current.FrameIndex,  // 帧索引
                PtsMs = current.PtsMs,        // 时间戳
                FrameUtcMs = current.FrameUtcMs,  // UTC 时间戳
                DebugNote = $"ignored_task; task={current.TaskId}"  // 调试注释
            };
        }

        // 构建窗口状态
        var windowState = SopWindowStateBuilder.Build(
            current,                              // 当前帧
            window,                               // 帧窗口
            context.Config.SopWindowMs,            // 窗口时长（毫秒）
            context.Config.SopMinScoreQ1000);      // 最低分数（千分比）
        // 将步骤按序号排序
        var orderedSteps = context.Steps.OrderBy(step => step.Step).ToList();

        // 尝试使用 SOP 流程规则评估
        if (SopProcedureRuleEvaluator.TryEvaluate(current, window, context, orderedSteps, out var procedureEvaluation))
        {
            // 流程规则匹配成功，返回结果
            return new AnalysisResult
            {
                Step = procedureEvaluation.Step,                    // 当前步骤
                Label = procedureEvaluation.CurrentStateCode,        // 标签
                Score = procedureEvaluation.Score,                  // 分数
                FrameIndex = current.FrameIndex,                   // 帧索引
                PtsMs = current.PtsMs,                           // 时间戳
                FrameUtcMs = current.FrameUtcMs,                   // UTC 时间戳
                DebugNote = procedureEvaluation.DebugNote,           // 调试注释
                CurrentStateCode = procedureEvaluation.CurrentStateCode,  // 当前状态代码
                ExpectedStateCode = procedureEvaluation.ExpectedStateCode,  // 期望状态代码
                NgReason = procedureEvaluation.NgReason,           // NG 原因
                IsSopCycleReset = procedureEvaluation.IsSopCycleReset,  // SOP 循环重置标记
                IsReset = procedureEvaluation.IsSopCycleReset     // 重置标记
            };
        }

        // 构建规则上下文
        var ruleContext = new SopRuleContext(current, context, windowState);
        // 获取所有满足的状态
        var satisfiedStates = SopSatisfiedStates.FromMatches(SopProjectRules.Match(ruleContext));
        // 解析步骤决策
        var decision = ResolveStep(current, orderedSteps, satisfiedStates, context.State, context.Config.HoldFrames);

        // 返回分析结果
        return new AnalysisResult
        {
            Step = decision.Step,                          // 当前步骤
            Label = decision.CurrentStateCode,               // 标签
            Score = decision.Score,                         // 分数
            FrameIndex = current.FrameIndex,                 // 帧索引
            PtsMs = current.PtsMs,                        // 时间戳
            FrameUtcMs = current.FrameUtcMs,                 // UTC 时间戳
            DebugNote = decision.DebugNote,                  // 调试注释
            CurrentStateCode = decision.CurrentStateCode,      // 当前状态代码
            ExpectedStateCode = decision.ExpectedStateCode,    // 期望状态代码
            NgReason = decision.NgReason                   // NG 原因
        };
    }

    // 私有静态方法，检查源任务是否允许
    private static bool IsSourceTaskAllowed(FsmFrameMetrics current, AnalysisConfig config)
    {
        // 如果配置中没有指定源任务 ID，允许所有任务
        if (string.IsNullOrWhiteSpace(config.SourceTaskId))
        {
            return true;
        }

        // 比较当前帧的任务 ID 和配置的任务 ID（忽略大小写）
        return string.Equals(current.TaskId, config.SourceTaskId.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    // 私有静态方法，解析当前步骤
    private static SopStepDecision ResolveStep(
        FsmFrameMetrics current,                    // 当前帧
        IReadOnlyList<FsmStepDefinition> orderedSteps,  // 排序后的步骤列表
        SopSatisfiedStates satisfiedStates,            // 满足的状态
        AnalysisState state,                        // 分析状态
        int holdFrames)                         // 保持帧数
    {
        // 如果没有步骤，返回无步骤
        if (orderedSteps.Count == 0)
        {
            state.ActiveStep = null;  // 清空当前步骤
            return SopStepDecision.None("no_steps");  // 返回无步骤决策
        }

        // 获取当前活动步骤的索引
        var activeIndex = state.ActiveStep.HasValue
            ? FindStepIndex(orderedSteps, state.ActiveStep.Value)  // 找到步骤索引
            : -1;  // 没有活动步骤，索引为 -1

        // 如果没有活动步骤
        if (activeIndex < 0)
        {
            // 查找第一个满足的步骤
            var firstSatisfied = FindFirstSatisfiedStep(orderedSteps, 0, satisfiedStates);
            if (firstSatisfied.HasValue)
            {
                // 找到第一个步骤，设置为活动步骤
                state.ActiveStep = firstSatisfied.Value.Step.Step;
                state.HoldCounter = Math.Max(0, holdFrames);  // 设置保持计数器
                return SopStepDecision.ForStep(firstSatisfied.Value.Step, firstSatisfied.Value.ExpectedStateCode, firstSatisfied.Value.State, "start");  // 返回开始决策
            }

            // 没有找到满足的步骤，清空活动步骤
            state.ActiveStep = null;
            return SopStepDecision.None("waiting_first_step");  // 返回等待第一个步骤决策
        }

        // 如果还有下一步
        if (activeIndex + 1 < orderedSteps.Count)
        {
            var nextStep = orderedSteps[activeIndex + 1];  // 获取下一步
            // 检查下一步是否满足
            if (TryGetSatisfiedState(nextStep, activeIndex + 1, satisfiedStates, out var nextExpected, out var nextState))
            {
                // 下一步满足，前进到下一步
                state.ActiveStep = nextStep.Step;
                state.HoldCounter = Math.Max(0, holdFrames);  // 重置保持计数器
                return SopStepDecision.ForStep(nextStep, nextExpected, nextState, "advance_next");  // 返回前进决策
            }
        }

        // 获取当前步骤
        var currentStep = orderedSteps[activeIndex];
        // 检查当前步骤是否仍然满足
        if (TryGetSatisfiedState(currentStep, activeIndex, satisfiedStates, out var currentExpected, out var currentState))
        {
            // 当前步骤满足，保持当前步骤
            state.ActiveStep = currentStep.Step;
            state.HoldCounter = Math.Max(0, holdFrames);  // 重置保持计数器
            return SopStepDecision.ForStep(currentStep, currentExpected, currentState, "hold_current");  // 返回保持决策
        }

        // 查找是否有跳步（跳过至少一步）
        var jumpedStep = FindFirstSatisfiedStep(orderedSteps, activeIndex + 2, satisfiedStates);
        if (jumpedStep.HasValue)
        {
            // 跳步，允许跳步
            state.ActiveStep = jumpedStep.Value.Step.Step;
            state.HoldCounter = Math.Max(0, holdFrames);  // 重置保持计数器
            return SopStepDecision.ForStep(
                jumpedStep.Value.Step,
                jumpedStep.Value.ExpectedStateCode,
                jumpedStep.Value.State,
                "jump_allowed");  // 不设 NgReason，允许跳步
        }

        // 如果保持计数器大于 0
        if (state.HoldCounter > 0)
        {
            state.HoldCounter--;  // 保持计数器减 1
            return SopStepDecision.ForStep(currentStep, ResolveExpectedStateCode(currentStep, activeIndex), null, "hold_counter");  // 返回保持决策
        }

        // 没有规则匹配，清空活动步骤
        state.ActiveStep = null;
        return SopStepDecision.None("no_rule_matched");  // 返回无规则匹配决策
    }

    // 私有静态方法，根据步骤号查找步骤索引
    private static int FindStepIndex(IReadOnlyList<FsmStepDefinition> orderedSteps, int step)
    {
        // 遍历所有步骤
        for (var i = 0; i < orderedSteps.Count; i++)
        {
            // 找到匹配的步骤号
            if (orderedSteps[i].Step == step)
            {
                return i;  // 返回索引
            }
        }

        // 没有找到，返回 -1
        return -1;
    }

    // 私有静态方法，从指定索引开始查找第一个满足的步骤
    // 返回值是可空的元组，包含步骤、期望状态代码和匹配状态
    private static (FsmStepDefinition Step, string ExpectedStateCode, SopMatchedState State)? FindFirstSatisfiedStep(
        IReadOnlyList<FsmStepDefinition> orderedSteps,  // 排序后的步骤列表
        int startIndex,                       // 开始索引
        SopSatisfiedStates satisfiedStates)       // 满足的状态
    {
        // 从开始索引遍历所有步骤
        for (var i = Math.Max(0, startIndex); i < orderedSteps.Count; i++)
        {
            // 检查步骤是否满足
            if (TryGetSatisfiedState(orderedSteps[i], i, satisfiedStates, out var expected, out var state))
            {
                return (orderedSteps[i], expected, state);  // 返回匹配的步骤
            }
        }

        // 没有找到，返回 null
        return null;
    }

    // 私有静态方法，尝试获取步骤的满足状态
    private static bool TryGetSatisfiedState(
        FsmStepDefinition step,         // 步骤定义
        int stepIndex,             // 步骤索引
        SopSatisfiedStates satisfiedStates,  // 满足的状态
        out string expectedStateCode,  // 输出：期望状态代码
        out SopMatchedState state)     // 输出：匹配的状态
    {
        // 解析期望状态代码
        expectedStateCode = ResolveExpectedStateCode(step, stepIndex);
        // 遍历所有分割后的期望状态代码
        foreach (var candidate in SplitExpectedStateCodes(expectedStateCode))
        {
            // 尝试从满足的状态中获取
            if (satisfiedStates.TryGet(candidate, out state!))
            {
                return true;  // 找到匹配
            }
        }

        // 没有找到匹配
        state = null!;
        return false;
    }

    // 私有静态方法，解析步骤的期望状态代码
    private static string ResolveExpectedStateCode(FsmStepDefinition step, int stepIndex)
    {
        // 如果步骤明确指定了期望状态代码
        if (!string.IsNullOrWhiteSpace(step.ExpectedStateCode))
        {
            return step.ExpectedStateCode.Trim();  // 返回修剪后的状态代码
        }

        // 尝试从步骤名称推断
        if (TryResolveExpectedStateCodeFromStepName(step.Name, out var inferred))
        {
            return inferred;  // 返回推断的状态代码
        }

        // 默认返回类别索引
        return $"class:{stepIndex}";
    }

    // 私有静态方法，尝试从步骤名称推断期望状态代码
    private static bool TryResolveExpectedStateCodeFromStepName(string? stepName, out string expectedStateCode)
    {
        // 归一化步骤名称
        var normalized = NormalizeStepName(stepName);
        // 如果名称为空
        if (string.IsNullOrWhiteSpace(normalized))
        {
            expectedStateCode = string.Empty;
            return false;
        }

        // 检查是否包含"产品"和"内盒"
        if (ContainsAll(normalized, "产品", "内盒") || ContainsAll(normalized, "product", "inner", "box"))
        {
            expectedStateCode = "product_in_inner_box";
            return true;
        }

        // 检查是否包含"圆片"和"内盒"
        if (ContainsAll(normalized, "圆片", "内盒") || ContainsAll(normalized, "disk", "inner", "box"))
        {
            expectedStateCode = "disk_in_inner_box";
            return true;
        }

        // 检查是否包含"充电器"和"外盒"
        if (ContainsAll(normalized, "充电器", "外盒") || ContainsAll(normalized, "charger", "outer", "box"))
        {
            expectedStateCode = "charger_in_outer_box";
            return true;
        }

        // 检查是否包含"保修卡"和"外盒"
        if (ContainsAll(normalized, "保修卡", "外盒") || ContainsAll(normalized, "warranty", "outer", "box"))
        {
            expectedStateCode = "warranty_card_in_outer_box";
            return true;
        }

        // 检查是否包含"内盒"和"外盒"
        if (ContainsAll(normalized, "内盒", "外盒") || ContainsAll(normalized, "inner", "outer", "box"))
        {
            expectedStateCode = "inner_box_in_outer_box";
            return true;
        }

        // 检查是否包含"内盒"
        if (normalized.Contains("内盒", StringComparison.OrdinalIgnoreCase) || ContainsAll(normalized, "inner", "box"))
        {
            expectedStateCode = "inner_box_visible";
            return true;
        }

        // 检查是否包含"圆片"或"圆盘"
        if (normalized.Contains("圆片", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("圆盘", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("disk", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("disc", StringComparison.OrdinalIgnoreCase))
        {
            expectedStateCode = "disk_visible";
            return true;
        }

        // 检查是否包含"产品"
        if (normalized.Contains("产品", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("product", StringComparison.OrdinalIgnoreCase))
        {
            expectedStateCode = "product_visible";
            return true;
        }

        // 检查是否包含"充电器"
        if (normalized.Contains("充电器", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("charger", StringComparison.OrdinalIgnoreCase))
        {
            expectedStateCode = "charger_visible";
            return true;
        }

        // 检查是否包含"保修卡"
        if (normalized.Contains("保修卡", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("warranty", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("card", StringComparison.OrdinalIgnoreCase))
        {
            expectedStateCode = "warranty_card_visible";
            return true;
        }

        // 检查是否包含"外盒"
        if (normalized.Contains("外盒", StringComparison.OrdinalIgnoreCase) || ContainsAll(normalized, "outer", "box"))
        {
            expectedStateCode = "outer_box_visible";
            return true;
        }

        // 没有匹配到
        expectedStateCode = string.Empty;
        return false;
    }

    // 私有静态方法，归一化步骤名称
    private static string NormalizeStepName(string? stepName)
    {
        // 如果为空或空白，返回空字符串；否则修剪并转为小写
        return string.IsNullOrWhiteSpace(stepName)
            ? string.Empty
            : stepName.Trim().ToLowerInvariant();
    }

    // 私有静态方法，检查文本是否包含所有指定的部分
    // params string[] 表示可变数量的参数
    private static bool ContainsAll(string text, params string[] parts)
    {
        // 遍历所有部分
        foreach (var part in parts)
        {
            // 如果文本不包含某部分
            if (!text.Contains(part, StringComparison.OrdinalIgnoreCase))
            {
                return false;  // 返回 false
            }
        }

        // 所有部分都包含
        return true;
    }

    // 私有静态方法，分割期望状态代码
    private static IEnumerable<string> SplitExpectedStateCodes(string expectedStateCode)
    {
        // 使用 | , ; 分割，移除空项并修剪
        return expectedStateCode
            .Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeStateCode);  // 归一化每个状态代码
    }

    // 公共静态方法，归一化状态代码
    public static string NormalizeStateCode(string value)
    {
        // 如果为空或空白，返回空字符串；否则修剪并转为小写
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
    }

    // 私有密封类，表示满足的状态集合
    private sealed class SopSatisfiedStates
    {
        // 私有只读字段，存储状态字典
        private readonly IReadOnlyDictionary<string, SopMatchedState> _states;

        // 构造函数
        public SopSatisfiedStates(IReadOnlyDictionary<string, SopMatchedState> states)
        {
            _states = states;  // 初始化状态字典
        }

        // 公共静态方法，从匹配的状态创建集合
        public static SopSatisfiedStates FromMatches(IEnumerable<SopMatchedState> matches)
        {
            // 创建不区分大小写的字典
            var states = new Dictionary<string, SopMatchedState>(StringComparer.OrdinalIgnoreCase);
            // 遍历所有匹配
            foreach (var match in matches)
            {
                // 如果状态代码为空，跳过
                if (string.IsNullOrWhiteSpace(match.StateCode))
                {
                    continue;
                }

                // 添加到字典（归一化状态代码）
                states[NormalizeStateCode(match.StateCode)] = match;
            }

            // 返回新的满足状态集合
            return new SopSatisfiedStates(states);
        }

        // 公共方法，尝试获取状态
        public bool TryGet(string stateCode, out SopMatchedState state)
        {
            // 尝试从字典获取（归一化状态代码）
            return _states.TryGetValue(NormalizeStateCode(stateCode), out state!);
        }
    }

    // 私有只读记录结构，表示步骤决策
    private readonly record struct SopStepDecision(
        int? Step,                          // 步骤号
        string? ExpectedStateCode,            // 期望状态代码
        string? CurrentStateCode,             // 当前状态代码
        double? Score,                      // 分数
        string DebugNote,                  // 调试注释
        string? NgReason)                 // NG 原因
    {
        // 公共静态方法，创建无步骤决策
        public static SopStepDecision None(string reason) => new(null, null, null, null, reason, null);

        // 公共静态方法，创建步骤决策
        public static SopStepDecision ForStep(
            FsmStepDefinition step,      // 步骤定义
            string expectedStateCode,    // 期望状态代码
            SopMatchedState? state,     // 匹配的状态
            string reason,              // 原因
            string? ngReason = null)       // NG 原因
        {
            // 当前状态代码：如果有匹配状态，归一化；否则 null
            var currentStateCode = state != null ? NormalizeStateCode(state.StateCode) : null;
            // 分数：如果有匹配状态，获取分数；否则 null
            var score = state?.Score;
            // 调试注释
            var note = state == null
                ? $"{reason}; expected={expectedStateCode}"  // 没有匹配状态
                : $"{reason}; expected={expectedStateCode}; current={currentStateCode}; score={state.Score:F3}; {state.Note}";  // 有匹配状态，F3 表示保留 3 位小数
            // 返回步骤决策
            return new SopStepDecision(step.Step, expectedStateCode, currentStateCode, score, note, ngReason);
        }
    }
}
