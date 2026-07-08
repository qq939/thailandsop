using System.Collections.Generic;

namespace VideoInferenceDemo;

public static class SopProjectRules
{
    public static IEnumerable<SopMatchedState> Match(SopRuleContext context)
    {
        foreach (var state in SopConditionHelpers.StableClassStates(
                     context.Window,
                     context.Analysis.Config.SopMinVisibleRatioQ1000))
        {
            yield return state;
        }

        foreach (var obj in context.Window.Objects)
        {
            if (!SopConditionHelpers.IsStable(obj, context.Analysis.Config.SopMinVisibleRatioQ1000))
            {
                continue;
            }

            if (TryResolveVisibleStateCode(obj.Label, obj.ClassId, out var stateCode))
            {
                yield return new SopMatchedState(stateCode, obj.Label, obj.BestScore, Object: obj);
            }
        }

        if (TryMatchContainedState(context, "内盒", "产品", "product_in_inner_box", out var productInInnerBox))
        {
            yield return productInInnerBox;
        }

        if (TryMatchContainedState(context, "内盒", "圆片", "disk_in_inner_box", out var diskInInnerBox) ||
            TryMatchContainedState(context, "内盒", "脚垫", "disk_in_inner_box", out diskInInnerBox))
        {
            yield return diskInInnerBox;
        }
    }

    private static bool TryMatchContainedState(
        SopRuleContext context,
        string containerLabel,
        string targetLabel,
        string stateCode,
        out SopMatchedState state)
    {
        if (SopConditionHelpers.TryGetObject(context.Window, containerLabel, out var container) &&
            SopConditionHelpers.TryGetObject(context.Window, targetLabel, out var target) &&
            SopConditionHelpers.IsStable(container, context.Analysis.Config.SopMinVisibleRatioQ1000) &&
            SopConditionHelpers.IsStable(target, context.Analysis.Config.SopMinVisibleRatioQ1000) &&
            container.BestBox is { } containerBox &&
            target.BestBox is { } targetBox &&
            SopConditionHelpers.CenterInside(containerBox, targetBox, marginPx: 20))
        {
            state = new SopMatchedState(
                stateCode,
                target.Label,
                target.BestScore,
                $"{targetLabel}_inside_{containerLabel}",
                target);
            return true;
        }

        state = null!;
        return false;
    }

    private static bool TryResolveVisibleStateCode(string? label, int classId, out string stateCode)
    {
        switch (SopRuleAnalysisStrategy.NormalizeStateCode(label ?? string.Empty))
        {
            case "内盒":
                stateCode = "inner_box_visible";
                return true;
            case "圆片":
            case "脚垫":
                stateCode = "disk_visible";
                return true;
            case "产品":
                stateCode = "product_visible";
                return true;
            case "充电器":
            case "适配器":
                stateCode = "charger_visible";
                return true;
            case "外盒":
                stateCode = "outer_box_visible";
                return true;
            case "保修卡":
                stateCode = "warranty_card_visible";
                return true;
        }

        switch (classId)
        {
            case 0:
                stateCode = "inner_box_visible";
                return true;
            case 1:
                stateCode = "disk_visible";
                return true;
            case 2:
                stateCode = "product_visible";
                return true;
            case 3:
                stateCode = "outer_box_visible";
                return true;
            case 4:
                stateCode = "charger_visible";
                return true;
            case 5:
                stateCode = "warranty_card_visible";
                return true;
            default:
                stateCode = string.Empty;
                return false;
        }
    }
}
