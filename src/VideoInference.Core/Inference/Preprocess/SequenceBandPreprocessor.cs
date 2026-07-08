using System;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace VideoInferenceDemo;

public sealed class SequenceBandPreprocessor : IModelPreprocessor<Mat, SequenceImageTransformContext>
{
    private readonly SequenceModelMetadata _metadata;
    private readonly string _inputName;
    private readonly float[] _mean;
    private readonly float[] _std;

    public SequenceBandPreprocessor(SequenceModelMetadata metadata, string inputName)
    {
        _metadata = metadata;
        _inputName = inputName;
        _mean = NormalizeArray(metadata.Preprocess.NormalizeMean, new[] { 0.485f, 0.456f, 0.406f });
        _std = NormalizeArray(metadata.Preprocess.NormalizeStd, new[] { 0.229f, 0.224f, 0.225f });
    }

    public ModelInput Preprocess(Mat image, out SequenceImageTransformContext context)
    {
        using var verticallyCropped = CropBottomRegion(image, out var geometry);
        using var aspectAdjusted = ApplyConfiguredPreprocess(verticallyCropped);
        using var resized = new Mat();
        Cv2.Resize(
            aspectAdjusted,
            resized,
            new Size(_metadata.InputWidth, _metadata.InputHeight),
            0,
            0,
            InterpolationFlags.LinearExact);

        using var converted = ConvertToBgrIfNeeded(resized);
        var bgr = converted ?? resized;
        using var normalized = new Mat();
        bgr.ConvertTo(normalized, MatType.CV_32FC3, 1.0 / 255.0);
        Cv2.Subtract(normalized, new Scalar(_mean[2], _mean[1], _mean[0]), normalized);
        Cv2.Divide(normalized, new Scalar(_std[2], _std[1], _std[0]), normalized);

        using var blob = CvDnn.BlobFromImage(
            normalized,
            1.0,
            new Size(_metadata.InputWidth, _metadata.InputHeight),
            Scalar.All(0),
            true,
            false);

        using var continuousClone = blob.IsContinuous() ? null : blob.Clone();
        var continuousBlob = continuousClone ?? blob;
        var elementCount = checked((int)continuousBlob.Total());
        var buffer = new float[elementCount];
        Marshal.Copy(continuousBlob.Data, buffer, 0, elementCount);

        context = new SequenceImageTransformContext(image.Width, image.Height, geometry);
        var tensor = new DenseTensor<float>(buffer, new[] { 1, 3, _metadata.InputHeight, _metadata.InputWidth });
        return new ModelInput(new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) });
    }

    private static Mat? ConvertToBgrIfNeeded(Mat image)
    {
        switch (image.Channels())
        {
            default:
                if (image.Channels() == 3)
                {
                    return null;
                }

                var bgr = new Mat();
                if (image.Channels() == 4)
                {
                    Cv2.CvtColor(image, bgr, ColorConversionCodes.BGRA2BGR);
                }
                else
                {
                    Cv2.CvtColor(image, bgr, ColorConversionCodes.GRAY2BGR);
                }

                return bgr;
        }
    }

    private Mat CropBottomRegion(Mat image, out SequenceInputGeometry geometry)
    {
        var cropRatio = Math.Clamp(_metadata.Preprocess.CropBottomRatio, 0.0, 0.999999);
        var keptHeight = (int)Math.Round(image.Height * (1.0 - cropRatio));
        keptHeight = Math.Clamp(keptHeight, 1, Math.Max(1, image.Height));
        geometry = new SequenceInputGeometry(0, keptHeight);

        if (keptHeight >= image.Height)
        {
            return new Mat(image, new Rect(0, 0, image.Width, image.Height));
        }

        var roi = new Rect(0, 0, image.Width, keptHeight);
        return new Mat(image, roi);
    }

    private Mat ApplyConfiguredPreprocess(Mat image)
    {
        var mode = _metadata.Preprocess.ImagePreprocess ?? "resize";
        if (!string.Equals(mode, "center_width_crop", StringComparison.OrdinalIgnoreCase))
        {
            return new Mat(image, new Rect(0, 0, image.Width, image.Height));
        }

        var targetHeight = Math.Max(1, _metadata.InputHeight);
        var targetWidth = Math.Max(1, _metadata.InputWidth);
        var desiredWidth = (int)Math.Round(image.Height * (targetWidth / (double)targetHeight));
        desiredWidth = Math.Clamp(desiredWidth, 1, Math.Max(1, image.Width));
        if (desiredWidth >= image.Width)
        {
            return new Mat(image, new Rect(0, 0, image.Width, image.Height));
        }

        var left = Math.Max(0, (image.Width - desiredWidth) / 2);
        var roi = new Rect(left, 0, desiredWidth, image.Height);
        return new Mat(image, roi);
    }

    private static float[] NormalizeArray(float[]? values, float[] fallback)
    {
        if (values is { Length: 3 })
        {
            return values;
        }

        return fallback;
    }
}

public readonly record struct SequenceImageTransformContext(
    int OriginalWidth,
    int OriginalHeight,
    SequenceInputGeometry Geometry);

public readonly record struct SequenceInputGeometry(int SourceTop, int SourceBottom)
{
    public int EffectiveHeight => Math.Max(1, SourceBottom - SourceTop);
}
