using System;
using System.Collections.Generic;
using System.Linq;

namespace VideoInferenceDemo;

internal static class SopProcedureHelpers
{
    internal sealed record DetectionSpec(params string[] Aliases);

    internal static readonly DetectionSpec InnerBox = new("内盒", "inner_box", "innerbox");
    internal static readonly DetectionSpec FootPad = new("圆片", "脚垫", "foot_pad", "footpad");
    internal static readonly DetectionSpec Product = new("产品", "product");
    internal static readonly DetectionSpec Charger = new("充电器", "适配器", "charger", "adapter");
    internal static readonly DetectionSpec OuterBox = new("外盒", "outer_box", "outerbox");
    internal static readonly DetectionSpec WarrantyCard = new("保修卡", "warranty_card", "warrantycard");

    internal static IReadOnlyList<FsmFrameMetrics> OrderFrames(IReadOnlyList<FsmFrameMetrics> frames, long currentPtsMs)
    {
        return (frames ?? Array.Empty<FsmFrameMetrics>())
            .Where(frame => frame.PtsMs <= currentPtsMs)
            .OrderBy(frame => frame.FrameIndex)
            .ThenBy(frame => frame.PtsMs)
            .ToArray();
    }

    internal static bool TryGetDetection(FsmFrameMetrics frame, DetectionSpec spec, out DetectionEntity detection)
    {
        detection = (frame.Detections ?? Array.Empty<DetectionEntity>())
            .Where(item => item != null && Matches(item, spec))
            .OrderByDescending(item => item.Score)
            .FirstOrDefault()!;

        return detection != null;
    }

    internal static bool ContainsObject(FsmFrameMetrics frame, DetectionSpec spec)
    {
        return (frame.Detections ?? Array.Empty<DetectionEntity>()).Any(item => item != null && Matches(item, spec));
    }

    internal static int CountConsecutiveFrames(
        IReadOnlyList<FsmFrameMetrics> frames,
        int requiredFrames,
        Func<FsmFrameMetrics, bool> predicate)
    {
        if (requiredFrames <= 0 || frames.Count == 0)
        {
            return 0;
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
                return count;
            }
        }

        return count;
    }

    internal static bool HasConsecutiveFrames(
        IReadOnlyList<FsmFrameMetrics> frames,
        int requiredFrames,
        Func<FsmFrameMetrics, bool> predicate)
    {
        return CountConsecutiveFrames(frames, requiredFrames, predicate) >= requiredFrames;
    }

    internal static bool HasDisallowedObjectInside(
        FsmFrameMetrics frame,
        DetectionSpec containerSpec,
        IReadOnlyList<DetectionSpec> allowedSpecs)
    {
        if (!TryGetDetection(frame, containerSpec, out var container))
        {
            return false;
        }

        var containerBox = ToBox(container);
        foreach (var detection in frame.Detections ?? Array.Empty<DetectionEntity>())
        {
            if (detection == null ||
                Matches(detection, containerSpec) ||
                allowedSpecs.Any(spec => Matches(detection, spec)))
            {
                continue;
            }

            if (ContainsCenter(containerBox, ToBox(detection)))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool HasDisallowedObjectInside(
        FsmFrameMetrics frame,
        DetectionSpec containerSpec,
        params DetectionSpec[] allowedSpecs)
    {
        return HasDisallowedObjectInside(frame, containerSpec, (IReadOnlyList<DetectionSpec>)allowedSpecs);
    }

    internal static bool IsDetectionInRelativeRegion(
        DetectionEntity container,
        DetectionEntity target,
        float minXRatio,
        float maxXRatio,
        float minYRatio,
        float maxYRatio)
    {
        var box = ToBox(container);
        var targetBox = ToBox(target);
        if (!IsDetectionInside(container, target, marginPx: 12f))
        {
            return false;
        }

        var centerX = (targetBox.X1 + targetBox.X2) * 0.5f;
        var centerY = (targetBox.Y1 + targetBox.Y2) * 0.5f;
        var width = Math.Max(1f, box.X2 - box.X1);
        var height = Math.Max(1f, box.Y2 - box.Y1);
        var relativeX = (centerX - box.X1) / width;
        var relativeY = (centerY - box.Y1) / height;

        return relativeX >= minXRatio &&
               relativeX <= maxXRatio &&
               relativeY >= minYRatio &&
               relativeY <= maxYRatio;
    }

    internal static bool IsDetectionInside(DetectionEntity container, DetectionEntity target, float marginPx = 0)
    {
        return SopConditionHelpers.IsInside(ToBox(container), ToBox(target), marginPx);
    }

    internal static bool IsMissingConsecutive(
        IReadOnlyList<FsmFrameMetrics> frames,
        DetectionSpec spec,
        int requiredFrames)
    {
        return HasConsecutiveFrames(frames, requiredFrames, frame => !ContainsObject(frame, spec));
    }

    internal static bool HasLargePositionJitter(
        IReadOnlyList<FsmFrameMetrics> frames,
        DetectionSpec spec,
        int requiredFrames,
        float thresholdPx)
    {
        if (frames.Count < requiredFrames || requiredFrames <= 1)
        {
            return false;
        }

        var tail = frames.Skip(Math.Max(0, frames.Count - requiredFrames)).ToArray();
        var centers = new List<(float X, float Y)>(requiredFrames);
        foreach (var frame in tail)
        {
            if (!TryGetDetection(frame, spec, out var detection))
            {
                return false;
            }

            var box = ToBox(detection);
            centers.Add(((box.X1 + box.X2) * 0.5f, (box.Y1 + box.Y2) * 0.5f));
        }

        var minX = centers.Min(item => item.X);
        var maxX = centers.Max(item => item.X);
        var minY = centers.Min(item => item.Y);
        var maxY = centers.Max(item => item.Y);
        return (maxX - minX) >= thresholdPx || (maxY - minY) >= thresholdPx;
    }

    internal static bool HasStablePosition(
        IReadOnlyList<FsmFrameMetrics> frames,
        DetectionSpec spec,
        int requiredFrames,
        float tolerancePx)
    {
        if (frames.Count < requiredFrames || requiredFrames <= 1)
        {
            return false;
        }

        var tail = frames.Skip(Math.Max(0, frames.Count - requiredFrames)).ToArray();
        DetectionEntity? first = null;
        foreach (var frame in tail)
        {
            if (!TryGetDetection(frame, spec, out var detection))
            {
                return false;
            }

            if (first == null)
            {
                first = detection;
                continue;
            }

            if (!IsApproximatelySameBox(ToBox(first), ToBox(detection), tolerancePx))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool HasOnlyStableObjectInHorizontalBand(
        IReadOnlyList<FsmFrameMetrics> frames,
        DetectionSpec spec,
        int requiredFrames,
        float minCenterXRatio,
        float maxCenterXRatio,
        float toleranceRatio)
    {
        if (frames.Count < requiredFrames || requiredFrames <= 1)
        {
            return false;
        }

        var tail = frames.Skip(Math.Max(0, frames.Count - requiredFrames)).ToArray();
        NormalizedBox? first = null;
        foreach (var frame in tail)
        {
            if (!TryGetOnlyDetection(frame, spec, out var detection))
            {
                return false;
            }

            if (!TryNormalizeBox(frame, detection, out var normalized))
            {
                return false;
            }

            var centerXRatio = (normalized.X1 + normalized.X2) * 0.5f;
            if (centerXRatio < minCenterXRatio || centerXRatio > maxCenterXRatio)
            {
                return false;
            }

            if (first == null)
            {
                first = normalized;
                continue;
            }

            if (!IsApproximatelySameNormalizedBox(first.Value, normalized, toleranceRatio))
            {
                return false;
            }
        }

        return true;
    }

    internal static double? AverageScore(
        IReadOnlyList<FsmFrameMetrics> frames,
        int requiredFrames,
        DetectionSpec spec,
        Func<FsmFrameMetrics, bool> predicate)
    {
        if (frames.Count < requiredFrames)
        {
            return null;
        }

        var scores = new List<float>(requiredFrames);
        for (var i = frames.Count - requiredFrames; i < frames.Count; i++)
        {
            var frame = frames[i];
            if (!predicate(frame) || !TryGetDetection(frame, spec, out var detection))
            {
                return null;
            }

            scores.Add(detection.Score);
        }

        return scores.Count == 0 ? null : scores.Average();
    }

    private static bool Matches(DetectionEntity detection, DetectionSpec spec)
    {
        var className = SopRuleAnalysisStrategy.NormalizeStateCode(detection.ClassName ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(className) &&
            spec.Aliases.Any(alias => className == SopRuleAnalysisStrategy.NormalizeStateCode(alias)))
        {
            return true;
        }

        return false;
    }

    internal static bool TryGetOnlyDetection(FsmFrameMetrics frame, DetectionSpec spec, out DetectionEntity detection)
    {
        detection = null!;
        var detections = (frame.Detections ?? Array.Empty<DetectionEntity>())
            .Where(item => item != null)
            .ToArray();

        if (detections.Length == 0 || detections.Any(item => !Matches(item, spec)))
        {
            return false;
        }

        detection = detections
            .OrderByDescending(item => item.Score)
            .First();
        return true;
    }

    private static bool TryNormalizeBox(FsmFrameMetrics frame, DetectionEntity detection, out NormalizedBox box)
    {
        box = default;
        var width = Math.Max(0, frame.FrameWidth);
        var height = Math.Max(0, frame.FrameHeight);
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        box = new NormalizedBox(
            detection.X1 / width,
            detection.Y1 / height,
            detection.X2 / width,
            detection.Y2 / height);
        return true;
    }

    private static SopBoundingBox ToBox(DetectionEntity detection)
    {
        return new SopBoundingBox(detection.X1, detection.Y1, detection.X2, detection.Y2);
    }

    private static bool ContainsCenter(SopBoundingBox container, SopBoundingBox target)
    {
        return SopConditionHelpers.CenterInside(container, target);
    }

    private static bool IsApproximatelySameBox(SopBoundingBox first, SopBoundingBox second, float tolerancePx)
    {
        return Math.Abs(first.X1 - second.X1) <= tolerancePx &&
               Math.Abs(first.Y1 - second.Y1) <= tolerancePx &&
               Math.Abs(first.X2 - second.X2) <= tolerancePx &&
               Math.Abs(first.Y2 - second.Y2) <= tolerancePx;
    }

    private static bool IsApproximatelySameNormalizedBox(NormalizedBox first, NormalizedBox second, float toleranceRatio)
    {
        return Math.Abs(first.X1 - second.X1) <= toleranceRatio &&
               Math.Abs(first.Y1 - second.Y1) <= toleranceRatio &&
               Math.Abs(first.X2 - second.X2) <= toleranceRatio &&
               Math.Abs(first.Y2 - second.Y2) <= toleranceRatio;
    }

    private readonly record struct NormalizedBox(float X1, float Y1, float X2, float Y2);
}
