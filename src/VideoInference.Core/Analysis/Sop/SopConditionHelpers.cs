using System;
using System.Collections.Generic;
using System.Linq;

namespace VideoInferenceDemo;

public static class SopConditionHelpers
{
    public static bool TryGetObject(
        SopWindowState window,
        string labelOrClassCode,
        out SopObjectWindowState obj)
    {
        ArgumentNullException.ThrowIfNull(window);
        var normalized = SopRuleAnalysisStrategy.NormalizeStateCode(labelOrClassCode);

        foreach (var item in window.Objects)
        {
            if (SopRuleAnalysisStrategy.NormalizeStateCode(item.Label) == normalized ||
                SopRuleAnalysisStrategy.NormalizeStateCode($"class:{item.ClassId}") == normalized)
            {
                obj = item;
                return true;
            }
        }

        obj = null!;
        return false;
    }

    public static bool IsStable(SopObjectWindowState? obj, int minVisibleRatioQ1000)
    {
        return obj != null && obj.VisibleRatioQ1000 >= Math.Clamp(minVisibleRatioQ1000, 0, 1000);
    }

    public static bool IsInside(SopBoundingBox container, SopBoundingBox target, float marginPx = 0)
    {
        return target.X1 >= container.X1 - marginPx &&
               target.Y1 >= container.Y1 - marginPx &&
               target.X2 <= container.X2 + marginPx &&
               target.Y2 <= container.Y2 + marginPx;
    }

    public static bool CenterInside(SopBoundingBox container, SopBoundingBox target, float marginPx = 0)
    {
        var cx = (target.X1 + target.X2) * 0.5f;
        var cy = (target.Y1 + target.Y2) * 0.5f;
        return cx >= container.X1 - marginPx &&
               cy >= container.Y1 - marginPx &&
               cx <= container.X2 + marginPx &&
               cy <= container.Y2 + marginPx;
    }

    public static float Area(SopBoundingBox box)
    {
        return Math.Max(0, box.X2 - box.X1) * Math.Max(0, box.Y2 - box.Y1);
    }

    public static float IntersectionOverTarget(SopBoundingBox container, SopBoundingBox target)
    {
        var ix1 = Math.Max(container.X1, target.X1);
        var iy1 = Math.Max(container.Y1, target.Y1);
        var ix2 = Math.Min(container.X2, target.X2);
        var iy2 = Math.Min(container.Y2, target.Y2);
        var intersection = Area(new SopBoundingBox(ix1, iy1, ix2, iy2));
        var targetArea = Area(target);
        return targetArea <= 0 ? 0 : intersection / targetArea;
    }

    public static IEnumerable<SopMatchedState> StableClassStates(
        SopWindowState window,
        int minVisibleRatioQ1000)
    {
        foreach (var obj in window.Objects.OrderBy(item => item.ClassId))
        {
            if (!IsStable(obj, minVisibleRatioQ1000))
            {
                continue;
            }

            yield return new SopMatchedState($"class:{obj.ClassId}", obj.Label, obj.BestScore, Object: obj);
            if (!string.IsNullOrWhiteSpace(obj.Label))
            {
                yield return new SopMatchedState(obj.Label, obj.Label, obj.BestScore, Object: obj);
            }
        }
    }
}
