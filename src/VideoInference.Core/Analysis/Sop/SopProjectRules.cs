// 引入 System.Collections.Generic 命名空间，包含泛型集合
using System.Collections.Generic;

// 定义命名空间
namespace VideoInferenceDemo;

// 定义静态类，包含项目的 SOP 规则
public static class SopProjectRules
{
    // 匹配所有满足条件的状态
    // IEnumerable<T> 表示可枚举的序列，yield return 用于延迟生成序列
    public static IEnumerable<SopMatchedState> Match(SopRuleContext context)
    {
        // 遍历所有稳定的类别状态
        foreach (var state in SopConditionHelpers.StableClassStates(
                     context.Window,                              // 窗口状态
                     context.Analysis.Config.SopMinVisibleRatioQ1000))  // 最小可见比例
        {
            yield return state;  // yield return 逐个返回元素，延迟执行
        }

        // 遍历窗口中的所有对象
        foreach (var obj in context.Window.Objects)
        {
            // 如果对象不稳定，跳过
            if (!SopConditionHelpers.IsStable(obj, context.Analysis.Config.SopMinVisibleRatioQ1000))
            {
                continue;  // continue 跳过本次循环剩余部分
            }

            // 尝试解析可见状态代码
            if (TryResolveVisibleStateCode(obj.Label, obj.ClassId, out var stateCode))
            {
                yield return new SopMatchedState(stateCode, obj.Label, obj.BestScore, Object: obj);  // 命名参数
            }
        }

        // 尝试匹配"产品在内盒里"的状态
        if (TryMatchContainedState(context, "内盒", "产品", "product_in_inner_box", out var productInInnerBox))
        {
            yield return productInInnerBox;
        }

        // 尝试匹配"圆片在内盒里"或"脚垫在内盒里"的状态
        // || 是逻辑或运算符，只要有一个为 true 结果就为 true
        if (TryMatchContainedState(context, "内盒", "圆片", "disk_in_inner_box", out var diskInInnerBox) ||
            TryMatchContainedState(context, "内盒", "脚垫", "disk_in_inner_box", out diskInInnerBox))
        {
            yield return diskInInnerBox;
        }
    }

    // 私有静态方法，尝试匹配"目标对象在容器对象里"的状态
    // out 参数用于输出结果
    private static bool TryMatchContainedState(
        SopRuleContext context,           // 规则上下文
        string containerLabel,            // 容器对象的标签
        string targetLabel,               // 目标对象的标签
        string stateCode,                 // 匹配成功时的状态代码
        out SopMatchedState state)        // 输出：匹配到的状态
    {
        // 检查所有条件是否都满足
        // && 是逻辑与运算符，所有条件都为 true 结果才为 true
        if (SopConditionHelpers.TryGetObject(context.Window, containerLabel, out var container) &&  // 尝试获取容器对象
            SopConditionHelpers.TryGetObject(context.Window, targetLabel, out var target) &&        // 尝试获取目标对象
            SopConditionHelpers.IsStable(container, context.Analysis.Config.SopMinVisibleRatioQ1000) &&  // 容器对象稳定
            SopConditionHelpers.IsStable(target, context.Analysis.Config.SopMinVisibleRatioQ1000) &&     // 目标对象稳定
            container.BestBox is { } containerBox &&  // is { } 是模式匹配，表示值不为 null
            target.BestBox is { } targetBox &&        // 模式匹配，目标边界框不为 null
            SopConditionHelpers.CenterInside(containerBox, targetBox, marginPx: 20))  // 目标中心点在容器内（20像素容差）
        {
            // 所有条件满足，创建匹配状态
            state = new SopMatchedState(
                stateCode,                              // 状态代码
                target.Label,                           // 目标标签
                target.BestScore,                       // 目标分数
                $"{targetLabel}_inside_{containerLabel}",  // 注释说明
                target);                                // 关联的对象
            return true;  // 返回匹配成功
        }

        // 匹配失败，设置输出为 null
        state = null!;  // null! 是 null-forgiving 运算符，表示我们确定这里不会是 null
        return false;   // 返回匹配失败
    }

    // 私有静态方法，尝试根据标签或类别 ID 解析可见状态代码
    private static bool TryResolveVisibleStateCode(string? label, int classId, out string stateCode)
    {
        // 根据标签进行匹配（先归一化）
        // switch 语句用于多分支选择
        switch (SopRuleAnalysisStrategy.NormalizeStateCode(label ?? string.Empty))
        {
            case "内盒":  // 标签是"内盒"
                stateCode = "inner_box_visible";
                return true;
            case "圆片":  // 标签是"圆片"
            case "脚垫":  // 标签是"脚垫"（两个 case 使用相同逻辑）
                stateCode = "disk_visible";
                return true;
            case "产品":  // 标签是"产品"
                stateCode = "product_visible";
                return true;
            case "充电器":  // 标签是"充电器"
            case "适配器":  // 标签是"适配器"
                stateCode = "charger_visible";
                return true;
            case "外盒":  // 标签是"外盒"
                stateCode = "outer_box_visible";
                return true;
            case "保修卡":  // 标签是"保修卡"
                stateCode = "warranty_card_visible";
                return true;
        }

        // 如果标签没有匹配到，根据类别 ID 匹配
        switch (classId)
        {
            case 0:  // 类别 ID 为 0
                stateCode = "inner_box_visible";
                return true;
            case 1:  // 类别 ID 为 1
                stateCode = "disk_visible";
                return true;
            case 2:  // 类别 ID 为 2
                stateCode = "product_visible";
                return true;
            case 3:  // 类别 ID 为 3
                stateCode = "outer_box_visible";
                return true;
            case 4:  // 类别 ID 为 4
                stateCode = "charger_visible";
                return true;
            case 5:  // 类别 ID 为 5
                stateCode = "warranty_card_visible";
                return true;
            default:  // 所有其他情况
                stateCode = string.Empty;
                return false;
        }
    }
}
