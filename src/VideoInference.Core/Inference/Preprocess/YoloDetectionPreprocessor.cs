using System;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using CvRect = OpenCvSharp.Rect;
using CvSize = OpenCvSharp.Size;

namespace VideoInferenceDemo;

public sealed class YoloDetectionPreprocessor : IModelPreprocessor<Mat, YoloImageTransformContext>
{
    private readonly string _inputName;
    private readonly int _inputWidth;
    private readonly int _inputHeight;

    public YoloDetectionPreprocessor(string inputName, int inputWidth, int inputHeight)
    {
        _inputName = inputName;
        _inputWidth = Math.Max(1, inputWidth);
        _inputHeight = Math.Max(1, inputHeight);
    }

    public ModelInput Preprocess(Mat image, out YoloImageTransformContext context)
    {
        using var converted = ConvertToBgrIfNeeded(image);
        var bgr = converted ?? image;
        var geometry = ImageAdjustmentGeometry.CreateLetterbox(bgr.Width, bgr.Height, _inputWidth, _inputHeight);

        using var resized = new Mat();
        Cv2.Resize(
            bgr,
            resized,
            new CvSize(
                Math.Max(1, (int)Math.Round(geometry.OriginalWidth * geometry.RatioX)),
                Math.Max(1, (int)Math.Round(geometry.OriginalHeight * geometry.RatioY))),
            0,
            0,
            InterpolationFlags.Linear);

        using var letterboxed = new Mat(_inputHeight, _inputWidth, MatType.CV_8UC3, new Scalar(114, 114, 114));
        var roi = new CvRect(
            geometry.PadX,
            geometry.PadY,
            Math.Max(1, (int)Math.Round(geometry.OriginalWidth * geometry.RatioX)),
            Math.Max(1, (int)Math.Round(geometry.OriginalHeight * geometry.RatioY)));
        using (var letterboxRoi = new Mat(letterboxed, roi))
        {
            resized.CopyTo(letterboxRoi);
        }

        using var blob = CvDnn.BlobFromImage(
            letterboxed,
            1.0 / 255.0,
            new CvSize(_inputWidth, _inputHeight),
            Scalar.All(0),
            true,
            false);

        using var continuousClone = blob.IsContinuous() ? null : blob.Clone();
        var continuousBlob = continuousClone ?? blob;
        var elementCount = checked((int)continuousBlob.Total());
        var buffer = new float[elementCount];
        Marshal.Copy(continuousBlob.Data, buffer, 0, elementCount);

        context = new YoloImageTransformContext(geometry);
        var tensor = new DenseTensor<float>(buffer, new[] { 1, 3, _inputHeight, _inputWidth });
        return new ModelInput(new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) });
    }

    private static Mat? ConvertToBgrIfNeeded(Mat image)
    {
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

public readonly record struct YoloImageTransformContext(ImageAdjustmentGeometry Geometry);
