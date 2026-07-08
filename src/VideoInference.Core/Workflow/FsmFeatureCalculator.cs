using System;
using System.Collections.Generic;

namespace VideoInferenceDemo;

public static class FsmFeatureCalculator
{
    public const int CenterClassId = 2;
    public const int MissingValue = 0xFFFF;

    public static FsmFrameFeatures Compute(FrameDetections item)
    {
        var frame = item.Frame;
        var width = frame.Width;
        var height = frame.Height;
        var diag2 = (width > 0 && height > 0) ? (double)width * width + (double)height * height : 0.0;

        DetectionEntity? center = null;
        foreach (var det in item.Detections)
        {
            if (det.ClassId != CenterClassId)
            {
                continue;
            }

            if (center == null || det.Score > center.Score)
            {
                center = det;
            }
        }

        if (center == null)
        {
            return new FsmFrameFeatures
            {
                DistId0ToId2Q1000 = MissingValue,
                DistId1ToId2Q1000 = MissingValue
            };
        }

        var centerArea = ComputeAreaPx(center);
        var centerCx = (center.X1 + center.X2) * 0.5;
        var centerCy = (center.Y1 + center.Y2) * 0.5;

        var best0 = FindBestTarget(item.Detections, 0, centerCx, centerCy);
        var best1 = FindBestTarget(item.Detections, 1, centerCx, centerCy);

        var result = new FsmFrameFeatures
        {
            CenterScoreQ1000 = ScoreToQ1000(center.Score),
            AreaId2Px = centerArea
        };

        if (best0 == null)
        {
            result = result with { DistId0ToId2Q1000 = MissingValue };
        }
        else
        {
            result = result with
            {
                DistId0ToId2Q1000 = DistToQ1000OrMissing(best0.Value.Dist2, diag2),
                ScoreId0Q1000 = ScoreToQ1000(best0.Value.Det.Score),
                AreaId0Px = ComputeAreaPx(best0.Value.Det)
            };
        }

        if (best1 == null)
        {
            result = result with { DistId1ToId2Q1000 = MissingValue };
        }
        else
        {
            result = result with
            {
                DistId1ToId2Q1000 = DistToQ1000OrMissing(best1.Value.Dist2, diag2),
                ScoreId1Q1000 = ScoreToQ1000(best1.Value.Det.Score),
                AreaId1Px = ComputeAreaPx(best1.Value.Det)
            };
        }

        return result;
    }

    public static int ScoreToQ1000(float score)
    {
        if (float.IsNaN(score) || float.IsInfinity(score))
        {
            return 0;
        }

        var scaled = (int)Math.Round(score * 1000.0, MidpointRounding.AwayFromZero);
        return Math.Clamp(scaled, 0, 1000);
    }

    private static int DistToQ1000(double dist2, double diag2)
    {
        if (double.IsNaN(dist2) || double.IsInfinity(dist2) || double.IsNaN(diag2) || double.IsInfinity(diag2) || diag2 <= 0)
        {
            return 1000;
        }

        var norm = dist2 / diag2;
        if (norm < 0)
        {
            norm = 0;
        }
        else if (norm > 1.0)
        {
            norm = 1.0;
        }

        var scaled = (int)Math.Round(Math.Sqrt(norm) * 1000.0, MidpointRounding.AwayFromZero);
        return Math.Clamp(scaled, 0, 1000);
    }

    private static int DistToQ1000OrMissing(double? dist2, double diag2)
    {
        if (!dist2.HasValue)
        {
            return MissingValue;
        }

        return DistToQ1000(dist2.Value, diag2);
    }

    private static int ComputeAreaPx(DetectionEntity det)
    {
        var w = Math.Max(0.0f, det.X2 - det.X1);
        var h = Math.Max(0.0f, det.Y2 - det.Y1);
        var area = w * h;
        if (float.IsNaN(area) || float.IsInfinity(area))
        {
            return 0;
        }

        var rounded = (int)Math.Round(area, MidpointRounding.AwayFromZero);
        return Math.Max(0, rounded);
    }

    private static (DetectionEntity Det, double Dist2)? FindBestTarget(
        IReadOnlyList<DetectionEntity> detections,
        int targetClassId,
        double centerCx,
        double centerCy)
    {
        DetectionEntity? best = null;
        double bestDist2 = double.MaxValue;
        foreach (var det in detections)
        {
            if (det.ClassId != targetClassId)
            {
                continue;
            }

            var cx = (det.X1 + det.X2) * 0.5;
            var cy = (det.Y1 + det.Y2) * 0.5;
            var dx = cx - centerCx;
            var dy = cy - centerCy;
            var dist2 = dx * dx + dy * dy;
            if (dist2 < bestDist2)
            {
                bestDist2 = dist2;
                best = det;
            }
        }

        if (best == null)
        {
            return null;
        }

        return (best, bestDist2);
    }
}
