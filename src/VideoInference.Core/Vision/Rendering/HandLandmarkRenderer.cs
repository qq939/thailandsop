using OpenCvSharp;
using CvPoint = OpenCvSharp.Point;

namespace VideoInferenceDemo;

public static class HandLandmarkRenderer
{
    private static readonly (int Start, int End)[] Connections =
    {
        (0, 1), (1, 2), (2, 3), (3, 4),
        (0, 5), (5, 6), (6, 7), (7, 8),
        (5, 9), (9, 10), (10, 11), (11, 12),
        (9, 13), (13, 14), (14, 15), (15, 16),
        (13, 17), (17, 18), (18, 19), (19, 20),
        (0, 17)
    };

    public static void Draw(Mat image, HandLandmarksPayload payload, VisionTaskRenderStyle style)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.Hands.Count == 0)
        {
            return;
        }

        var lineThickness = Math.Max(1, style.BoxThickness);
        var pointRadius = Math.Max(2, style.BoxThickness + 1);

        for (var handIndex = 0; handIndex < payload.Hands.Count; handIndex++)
        {
            var hand = payload.Hands[handIndex];
            var color = ResolveColor(style, handIndex);

            foreach (var (start, end) in Connections)
            {
                if (!TryGetPoint(hand, start, image.Width, image.Height, out var p0) ||
                    !TryGetPoint(hand, end, image.Width, image.Height, out var p1))
                {
                    continue;
                }

                Cv2.Line(image, p0, p1, color, lineThickness);
            }

            foreach (var point in hand.Points)
            {
                if (!TryGetPoint(point, image.Width, image.Height, out var pixel))
                {
                    continue;
                }

                Cv2.Circle(image, pixel, pointRadius, color, -1);
            }

            if (TryGetPoint(hand, 0, image.Width, image.Height, out var wrist))
            {
                var label = string.IsNullOrWhiteSpace(hand.Handedness)
                    ? $"Hand {hand.Score:P1}"
                    : $"{hand.Handedness} {hand.Score:P1}";
                var textPoint = new CvPoint(wrist.X + 6, Math.Max(18, wrist.Y - 10));
                Cv2.PutText(image, label, textPoint, HersheyFonts.HersheySimplex, style.LabelFontScale, color, lineThickness);
            }
        }
    }

    private static bool TryGetPoint(HandLandmarkSet hand, int index, int width, int height, out CvPoint point)
    {
        var candidate = hand.Points.FirstOrDefault(item => item.Index == index);
        if (candidate == null)
        {
            point = default;
            return false;
        }

        return TryGetPoint(candidate, width, height, out point);
    }

    private static bool TryGetPoint(HandLandmarkPoint point, int width, int height, out CvPoint pixel)
    {
        pixel = default;
        if (point is null)
        {
            return false;
        }

        var x = (int)Math.Round(point.X * Math.Max(1, width - 1));
        var y = (int)Math.Round(point.Y * Math.Max(1, height - 1));
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return false;
        }

        pixel = new CvPoint(x, y);
        return true;
    }

    private static Scalar ResolveColor(VisionTaskRenderStyle style, int index)
    {
        if (index >= 0 &&
            style.OverridesByClass != null &&
            index < style.OverridesByClass.Length &&
            style.OverridesByClass[index].HasValue)
        {
            return style.OverridesByClass[index]!.Value;
        }

        return style.GlobalOverride ?? GetColorFromIndex(index);
    }

    private static Scalar GetColorFromIndex(int index)
    {
        return index % 2 == 0
            ? new Scalar(0, 220, 120)
            : new Scalar(255, 180, 0);
    }
}
