using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenCvSharp;
using CvPoint = OpenCvSharp.Point;
using CvRect = OpenCvSharp.Rect;
using DrawingPointF = System.Drawing.PointF;

namespace VideoInferenceDemo;

internal readonly record struct PipelineDrawStyle(
    Scalar? GlobalOverride,
    Scalar?[]? OverridesByClass,
    int BoxThickness,
    double LabelFontScale);

internal static class PipelineFrameAnnotator
{
    public static int ResolveBoxThickness(int? value)
    {
        return Math.Clamp(value ?? 2, 1, 12);
    }

    public static double ResolveLabelFontScale(double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            return 0.55;
        }

        return Math.Clamp(value.Value, 0.2, 5.0);
    }

    public static Scalar?[]? ParseColorOverrides(string[]? colors)
    {
        if (colors is not { Length: > 0 })
        {
            return null;
        }

        var overrides = new Scalar?[colors.Length];
        var hasAny = false;

        for (var i = 0; i < colors.Length; i++)
        {
            if (!TryParseColor(colors[i], out var color))
            {
                continue;
            }

            overrides[i] = color;
            hasAny = true;
        }

        return hasAny ? overrides : null;
    }

    public static bool TryParseColor(string? value, out Scalar color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var raw = value.Trim();
        if (raw.StartsWith("#", StringComparison.Ordinal))
        {
            raw = raw[1..];
        }

        if (raw.Length == 6 &&
            int.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            var r = (byte)((rgb >> 16) & 0xFF);
            var g = (byte)((rgb >> 8) & 0xFF);
            var b = (byte)(rgb & 0xFF);
            color = new Scalar(b, g, r);
            return true;
        }

        var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
        {
            return false;
        }

        if (!byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var blue))
        {
            return false;
        }

        color = new Scalar(blue, green, red);
        return true;
    }

    public static void DrawDetections(Mat image, IReadOnlyList<YoloDetection> results, PipelineDrawStyle style)
    {
        if (results.Count == 0)
        {
            return;
        }

        var labelThickness = Math.Max(1, style.BoxThickness);
        foreach (var det in results)
        {
            if (!TryGetRect(det.X1, det.Y1, det.X2 - det.X1, det.Y2 - det.Y1, image.Width, image.Height, out var rect))
            {
                continue;
            }

            var color = ResolveDrawColor(det.ClassId, style.OverridesByClass, style.GlobalOverride);
            Cv2.Rectangle(image, rect, color, style.BoxThickness);

            var label = string.IsNullOrWhiteSpace(det.ClassName)
                ? $"#{det.ClassId}"
                : $"{det.ClassName} {det.Score:P1}";

            var labelPoint = new CvPoint(rect.X, Math.Max(0, rect.Y - 6));
            DrawLabel(image, label, labelPoint, style.LabelFontScale, color, labelThickness);
        }
    }

    public static void DrawSegmentations(Mat image, IReadOnlyList<YoloSegmentation> results, PipelineDrawStyle style)
    {
        if (results.Count == 0)
        {
            return;
        }

        foreach (var det in results)
        {
            if (det.Mask.Length == 0 ||
                det.MaskWidth != image.Width ||
                det.MaskHeight != image.Height)
            {
                continue;
            }

            var color = ResolveDrawColor(det.ClassId, style.OverridesByClass, style.GlobalOverride);
            BlendMask(image, det.Mask, color, 0.35);
        }

        var labelThickness = Math.Max(1, style.BoxThickness);
        foreach (var det in results)
        {
            if (!TryGetRect(det.X1, det.Y1, det.X2 - det.X1, det.Y2 - det.Y1, image.Width, image.Height, out var rect))
            {
                continue;
            }

            var color = ResolveDrawColor(det.ClassId, style.OverridesByClass, style.GlobalOverride);
            Cv2.Rectangle(image, rect, color, style.BoxThickness);

            var label = string.IsNullOrWhiteSpace(det.ClassName)
                ? $"#{det.ClassId}"
                : $"{det.ClassName} {det.Score:P1}";

            var labelPoint = new CvPoint(rect.X, Math.Max(0, rect.Y - 6));
            DrawLabel(image, label, labelPoint, style.LabelFontScale, color, labelThickness);
        }
    }

    public static void DrawUnetSegmentation(
        Mat image,
        UnetSegmentationResult result,
        int classId,
        string className,
        PipelineDrawStyle style)
    {
        if (result.Components.Count == 0 ||
            result.Mask.Length == 0 ||
            result.MaskWidth != image.Width ||
            result.MaskHeight != image.Height)
        {
            return;
        }

        var color = ResolveDrawColor(classId, style.OverridesByClass, style.GlobalOverride);
        BlendMask(image, result.Mask, color, 0.35);

        var labelThickness = Math.Max(1, style.BoxThickness);
        foreach (var component in result.Components)
        {
            if (!TryGetRect(component.X, component.Y, component.Width, component.Height, image.Width, image.Height, out var rect))
            {
                continue;
            }

            Cv2.Rectangle(image, rect, color, style.BoxThickness);
            var label = string.IsNullOrWhiteSpace(className)
                ? $"#{classId} {component.MaxProbability:P1}"
                : $"{className} {component.MaxProbability:P1}";
            var labelPoint = new CvPoint(rect.X, Math.Max(0, rect.Y - 6));
            DrawLabel(image, label, labelPoint, style.LabelFontScale, color, labelThickness);
        }
    }

    public static void DrawOcrText(Mat image, string text, int roiX, int roiY, int roiW, int roiH)
    {
        if (string.IsNullOrWhiteSpace(text) || image.Empty())
        {
            return;
        }

        var labelPoint = new CvPoint(
            Math.Max(0, roiX),
            Math.Max(0, roiY - 6));
        DrawLabel(image, text, labelPoint, 0.6, new Scalar(0, 255, 0), 2);

        // Draw a dashed rectangle around the ROI to indicate OCR area
        Cv2.Rectangle(image, new CvRect(
            Math.Max(0, roiX),
            Math.Max(0, roiY),
            Math.Min(roiW, image.Width - roiX),
            Math.Min(roiH, image.Height - roiY)),
            new Scalar(0, 255, 0), 1);
    }

    public static void DrawSequenceBands(Mat image, IReadOnlyList<SequenceBandPrediction> bands, PipelineDrawStyle style)
    {
        if (bands.Count == 0)
        {
            return;
        }

        using var overlay = image.Clone();
        foreach (var band in bands)
        {
            if (!TryGetRect(band.X0, band.Y0, band.X1 - band.X0, band.Y1 - band.Y0, image.Width, image.Height, out var rect))
            {
                continue;
            }

            var color = ResolveDrawColor(band.ClassId, style.OverridesByClass, style.GlobalOverride);
            Cv2.Rectangle(overlay, rect, color, -1);
        }

        Cv2.AddWeighted(overlay, 0.18, image, 0.82, 0, image);

        var labelThickness = Math.Max(1, style.BoxThickness);
        foreach (var band in bands)
        {
            if (!TryGetRect(band.X0, band.Y0, band.X1 - band.X0, band.Y1 - band.Y0, image.Width, image.Height, out var rect))
            {
                continue;
            }

            var color = ResolveDrawColor(band.ClassId, style.OverridesByClass, style.GlobalOverride);
            Cv2.Rectangle(image, rect, color, style.BoxThickness);

            var label = string.IsNullOrWhiteSpace(band.ClassName)
                ? $"#{band.ClassId}"
                : $"{band.ClassName} {band.Confidence:P1}";
            var labelPoint = new CvPoint(rect.X + 6, Math.Max(18, rect.Y + 22));
            DrawLabel(image, label, labelPoint, style.LabelFontScale, color, labelThickness);
        }
    }

    private static void DrawLabel(Mat image, string label, CvPoint point, double fontScale, Scalar color, int thickness)
    {
        if (CanUseOpenCvText(label) || !OperatingSystem.IsWindowsVersionAtLeast(6, 1) || !TryDrawUnicodeLabel(image, label, point, fontScale, color))
        {
            Cv2.PutText(image, ToOpenCvSafeText(label), point, HersheyFonts.HersheySimplex, fontScale, color, thickness);
        }
    }

    private static bool CanUseOpenCvText(string text)
    {
        return text.All(ch => ch <= 0x7F);
    }

    private static string ToOpenCvSafeText(string text)
    {
        if (CanUseOpenCvText(text))
        {
            return text;
        }

        var chars = text.Select(ch => ch <= 0x7F ? ch : '?').ToArray();
        return new string(chars);
    }

    [SupportedOSPlatform("windows6.1")]
    private static bool TryDrawUnicodeLabel(Mat image, string label, CvPoint point, double fontScale, Scalar color)
    {
        if (image.Empty() || image.Type() != MatType.CV_8UC3)
        {
            return false;
        }

        try
        {
            using var sourceClone = image.IsContinuous() ? null : image.Clone();
            var source = sourceClone ?? image;
            using var bitmap = CreateBitmapFromBgr(source);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var font = new Font("Microsoft YaHei UI", ResolveUnicodeFontSize(fontScale), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(ToDrawingColor(color)))
            {
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                graphics.DrawString(label, font, brush, new DrawingPointF(point.X, Math.Max(0, point.Y - ResolveUnicodeFontSize(fontScale))));
            }

            CopyBitmapToBgr(bitmap, source);
            if (!ReferenceEquals(source, image))
            {
                source.CopyTo(image);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static float ResolveUnicodeFontSize(double fontScale)
    {
        return (float)Math.Clamp(fontScale * 28.0, 10.0, 96.0);
    }

    [SupportedOSPlatform("windows6.1")]
    private static Color ToDrawingColor(Scalar color)
    {
        return Color.FromArgb(
            ClampByte(color.Val2),
            ClampByte(color.Val1),
            ClampByte(color.Val0));
    }

    private static int ClampByte(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return (int)Math.Clamp(Math.Round(value), 0, 255);
    }

    [SupportedOSPlatform("windows6.1")]
    private static Bitmap CreateBitmapFromBgr(Mat image)
    {
        var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format24bppRgb);

        try
        {
            CopyRows(image.Data, checked((int)image.Step()), data.Scan0, data.Stride, image.Height, image.Width * 3);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    [SupportedOSPlatform("windows6.1")]
    private static void CopyBitmapToBgr(Bitmap bitmap, Mat image)
    {
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);

        try
        {
            CopyRows(data.Scan0, data.Stride, image.Data, checked((int)image.Step()), image.Height, image.Width * 3);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void CopyRows(IntPtr source, int sourceStride, IntPtr destination, int destinationStride, int height, int bytesPerRow)
    {
        var row = new byte[bytesPerRow];
        for (var y = 0; y < height; y++)
        {
            Marshal.Copy(IntPtr.Add(source, y * sourceStride), row, 0, bytesPerRow);
            Marshal.Copy(row, 0, IntPtr.Add(destination, y * destinationStride), bytesPerRow);
        }
    }

    private static bool TryGetRect(double x, double y, double w, double h, int maxWidth, int maxHeight, out CvRect rect)
    {
        rect = default;
        if (maxWidth <= 0 || maxHeight <= 0)
        {
            return false;
        }

        var ix = ClampToInt(x, 0, maxWidth - 1);
        var iy = ClampToInt(y, 0, maxHeight - 1);
        var iw = ClampToInt(w, 1, maxWidth - ix);
        var ih = ClampToInt(h, 1, maxHeight - iy);
        rect = new CvRect(ix, iy, iw, ih);
        return true;
    }

    private static int ClampToInt(double value, int min, int max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return min;
        }

        var rounded = (int)Math.Round(value);
        if (rounded < min)
        {
            return min;
        }

        return rounded > max ? max : rounded;
    }

    private static Scalar ResolveDrawColor(int classId, Scalar?[]? overridesByClass, Scalar? globalOverride)
    {
        if (classId >= 0 &&
            overridesByClass != null &&
            classId < overridesByClass.Length)
        {
            var colorOverride = overridesByClass[classId];
            if (colorOverride.HasValue)
            {
                return colorOverride.Value;
            }
        }

        return globalOverride ?? GetColorFromId(classId);
    }

    private static void BlendMask(Mat image, byte[] mask, Scalar color, double alpha)
    {
        if (image.Channels() != 3 || image.Type() != MatType.CV_8UC3)
        {
            return;
        }

        using var maskMat = Mat.FromPixelData(image.Height, image.Width, MatType.CV_8UC1, mask);
        using var colorLayer = new Mat(image.Size(), image.Type(), color);
        using var blended = new Mat();
        Cv2.AddWeighted(colorLayer, alpha, image, 1.0 - alpha, 0, blended);
        blended.CopyTo(image, maskMat);
    }

    private static Scalar GetColorFromId(int id)
    {
        var seed = id * 1103515245 + 12345;
        var r = (byte)((seed >> 16) & 0xFF);
        var g = (byte)((seed >> 8) & 0xFF);
        var b = (byte)(seed & 0xFF);
        return new Scalar(b, g, r);
    }
}
