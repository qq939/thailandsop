// 引入 System 命名空间，包含基本类型和工具函数
using System;
// 引入 System.Collections.Generic 命名空间，包含泛型集合
using System.Collections.Generic;
// 引入 System.Linq 命名空间，包含 LINQ 查询操作
using System.Linq;

// 定义命名空间
namespace VideoInferenceDemo;

// 定义静态类，包含 SOP 条件判断的辅助方法
public static class SopConditionHelpers
{
    // 尝试从窗口中根据标签或类别代码获取对象
    public static bool TryGetObject(
        SopWindowState window,                  // 窗口状态
        string labelOrClassCode,                // 标签或类别代码
        out SopObjectWindowState obj)           // 输出：找到的对象
    {
        // 检查参数是否为 null，如果是则抛出异常
        // ArgumentNullException.ThrowIfNull 是 .NET 6 引入的便捷方法
        ArgumentNullException.ThrowIfNull(window);
        // 归一化标签或类别代码
        var normalized = SopRuleAnalysisStrategy.NormalizeStateCode(labelOrClassCode);

        // 遍历窗口中的所有对象
        foreach (var item in window.Objects)
        {
            // 比较归一化后的标签，或者比较类别代码
            if (SopRuleAnalysisStrategy.NormalizeStateCode(item.Label) == normalized ||
                SopRuleAnalysisStrategy.NormalizeStateCode($"class:{item.ClassId}") == normalized)
            {
                obj = item;  // 找到匹配的对象
                return true;  // 返回成功
            }
        }

        // 没有找到匹配的对象
        obj = null!;  // null! 是 null-forgiving 运算符
        return false;  // 返回失败
    }

    // 判断对象是否稳定
    public static bool IsStable(SopObjectWindowState? obj, int minVisibleRatioQ1000)
    {
        // 对象不为 null，并且可见比例大于等于最小阈值
        return obj != null && obj.VisibleRatioQ1000 >= Math.Clamp(minVisibleRatioQ1000, 0, 1000);
    }

    // 判断目标边界框是否在容器边界框内部
    public static bool IsInside(SopBoundingBox container, SopBoundingBox target, float marginPx = 0)
    {
        // 目标的左边 >= 容器左边 - 边距
        // 目标的上边 >= 容器上边 - 边距
        // 目标的右边 <= 容器右边 + 边距
        // 目标的下边 <= 容器下边 + 边距
        return target.X1 >= container.X1 - marginPx &&
               target.Y1 >= container.Y1 - marginPx &&
               target.X2 <= container.X2 + marginPx &&
               target.Y2 <= container.Y2 + marginPx;
    }

    // 判断目标的中心点是否在容器边界框内部
    public static bool CenterInside(SopBoundingBox container, SopBoundingBox target, float marginPx = 0)
    {
        // 计算目标的中心 X 坐标
        var cx = (target.X1 + target.X2) * 0.5f;
        // 计算目标的中心 Y 坐标
        var cy = (target.Y1 + target.Y2) * 0.5f;
        // 判断中心点是否在容器内（带容差）
        return cx >= container.X1 - marginPx &&
               cy >= container.Y1 - marginPx &&
               cx <= container.X2 + marginPx &&
               cy <= container.Y2 + marginPx;
    }

    // 计算边界框的面积
    public static float Area(SopBoundingBox box)
    {
        // 宽度 * 高度，确保不小于 0
        return Math.Max(0, box.X2 - box.X1) * Math.Max(0, box.Y2 - box.Y1);
    }

    // 计算交集面积占目标面积的比例（Intersection over Target）
    public static float IntersectionOverTarget(SopBoundingBox container, SopBoundingBox target)
    {
        // 计算交集边界框的坐标
        var ix1 = Math.Max(container.X1, target.X1);  // 交集左边界
        var iy1 = Math.Max(container.Y1, target.Y1);  // 交集上边界
        var ix2 = Math.Min(container.X2, target.X2);  // 交集右边界
        var iy2 = Math.Min(container.Y2, target.Y2);  // 交集下边界
        // 计算交集面积
        var intersection = Area(new SopBoundingBox(ix1, iy1, ix2, iy2));
        // 计算目标面积
        var targetArea = Area(target);
        // 如果目标面积 <= 0，返回 0；否则返回比例
        return targetArea <= 0 ? 0 : intersection / targetArea;
    }

    // 获取所有稳定类别的状态
    public static IEnumerable<SopMatchedState> StableClassStates(
        SopWindowState window,                  // 窗口状态
        int minVisibleRatioQ1000)               // 最小可见比例
    {
        // 按类别 ID 排序后遍历所有对象
        foreach (var obj in window.Objects.OrderBy(item => item.ClassId))
        {
            // 如果对象不稳定，跳过
            if (!IsStable(obj, minVisibleRatioQ1000))
            {
                continue;  // continue 跳过本次循环剩余部分
            }

            // 返回类别代码作为状态代码
            yield return new SopMatchedState($"class:{obj.ClassId}", obj.Label, obj.BestScore, Object: obj);
            // 如果有标签，也返回标签作为状态代码
            if (!string.IsNullOrWhiteSpace(obj.Label))
            {
                yield return new SopMatchedState(obj.Label, obj.Label, obj.BestScore, Object: obj);
            }
        }
    }
}
