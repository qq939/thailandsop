using OpenCvSharp;

namespace VideoInferenceDemo.ImageInspection.Runtime;

public static class InspectionResultDisplayRenderer
{
    private static readonly Scalar DefectColor = new(80, 83, 239);

    public static bool HasSegmentationOverlays(InspectionCycleResult? result)
    {
        return result?.RoiResults.Any(HasMask) == true;
    }

    public static void DrawSegmentationOverlays(Mat image, InspectionCycleResult? result)
    {
        if (image.Empty() || image.Type() != MatType.CV_8UC3 || result == null)
        {
            return;
        }

        var roisById = result.ResolvedRois.ToDictionary(roi => roi.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var roiResult in result.RoiResults.Where(HasMask))
        {
            if (!roisById.TryGetValue(roiResult.RoiId, out var roi))
            {
                continue;
            }

            using var warpedMask = WarpMaskToImage(image, roi, roiResult);
            if (warpedMask.Empty())
            {
                continue;
            }

            BlendMask(image, warpedMask, DefectColor, 0.35);
            using var contourMask = warpedMask.Clone();
            Cv2.FindContours(contourMask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            Cv2.DrawContours(image, contours, -1, DefectColor, 2);
        }
    }

    private static bool HasMask(InspectionRoiResult result)
    {
        return result.SegmentationMask is { Length: > 0 } &&
               result.SegmentationMaskWidth > 0 &&
               result.SegmentationMaskHeight > 0 &&
               result.SegmentationMask.Length == result.SegmentationMaskWidth * result.SegmentationMaskHeight;
    }

    private static Mat WarpMaskToImage(Mat image, RoiDefinition roi, InspectionRoiResult result)
    {
        var mask = result.SegmentationMask;
        if (mask == null)
        {
            return new Mat();
        }

        var width = result.SegmentationMaskWidth;
        var height = result.SegmentationMaskHeight;
        if (width <= 0 || height <= 0)
        {
            return new Mat();
        }

        using var roiMask = Mat.FromPixelData(height, width, MatType.CV_8UC1, mask);
        var target = new Mat(image.Height, image.Width, MatType.CV_8UC1, Scalar.All(0));
        var source = new[]
        {
            new Point2f(0, 0),
            new Point2f(Math.Max(0, width - 1), 0),
            new Point2f(0, Math.Max(0, height - 1))
        };
        var destination = ResolveRoiImagePoints(image.Width, image.Height, roi);
        using var affine = Cv2.GetAffineTransform(source, destination);
        Cv2.WarpAffine(
            roiMask,
            target,
            affine,
            new Size(image.Width, image.Height),
            InterpolationFlags.Nearest,
            BorderTypes.Constant,
            Scalar.All(0));
        return target;
    }

    private static Point2f[] ResolveRoiImagePoints(int imageWidth, int imageHeight, RoiDefinition roi)
    {
        var center = new Point2d(roi.CenterX * imageWidth, roi.CenterY * imageHeight);
        var width = Math.Max(1, (int)Math.Round(roi.Width * imageWidth));
        var height = Math.Max(1, (int)Math.Round(roi.Height * imageHeight));
        var radians = roi.AngleDeg * Math.PI / 180.0;
        var axisX = new Point2d(Math.Cos(radians), Math.Sin(radians));
        var axisY = new Point2d(-Math.Sin(radians), Math.Cos(radians));
        var halfWidth = width / 2.0;
        var halfHeight = height / 2.0;

        return
        [
            ToPoint2f(center - (axisX * halfWidth) - (axisY * halfHeight)),
            ToPoint2f(center + (axisX * halfWidth) - (axisY * halfHeight)),
            ToPoint2f(center - (axisX * halfWidth) + (axisY * halfHeight))
        ];
    }

    private static void BlendMask(Mat image, Mat mask, Scalar color, double alpha)
    {
        using var colorLayer = new Mat(image.Size(), image.Type(), color);
        using var blended = new Mat();
        Cv2.AddWeighted(colorLayer, alpha, image, 1.0 - alpha, 0, blended);
        blended.CopyTo(image, mask);
    }

    private static Point2f ToPoint2f(Point2d point)
    {
        return new Point2f((float)point.X, (float)point.Y);
    }
}
