using System;
using System.Collections.Generic;
using System.Linq;

namespace VideoInferenceDemo;

internal readonly record struct RectDetectionCandidate(
    int ClassId,
    float Score,
    float X,
    float Y,
    float Width,
    float Height);

internal static class RectDetectionNms
{
    public static List<RectDetectionCandidate> Run(IReadOnlyList<RectDetectionCandidate> candidates, float iouThreshold)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        if (candidates.Count == 1)
        {
            return [candidates[0]];
        }

        var kept = new List<RectDetectionCandidate>(candidates.Count);
        foreach (var candidate in candidates.OrderByDescending(item => item.Score))
        {
            var keep = true;
            foreach (var existing in kept)
            {
                if (existing.ClassId != candidate.ClassId)
                {
                    continue;
                }

                if (CalculateIntersectionOverUnion(existing, candidate) > iouThreshold)
                {
                    keep = false;
                    break;
                }
            }

            if (keep)
            {
                kept.Add(candidate);
            }
        }

        return kept;
    }

    private static float CalculateIntersectionOverUnion(RectDetectionCandidate a, RectDetectionCandidate b)
    {
        var areaA = a.Width * a.Height;
        if (areaA <= 0f)
        {
            return 0f;
        }

        var areaB = b.Width * b.Height;
        if (areaB <= 0f)
        {
            return 0f;
        }

        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.X + a.Width, b.X + b.Width);
        var bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);

        var intersectionWidth = Math.Max(0f, right - left);
        var intersectionHeight = Math.Max(0f, bottom - top);
        var intersectionArea = intersectionWidth * intersectionHeight;
        if (intersectionArea <= 0f)
        {
            return 0f;
        }

        return intersectionArea / (areaA + areaB - intersectionArea);
    }
}
