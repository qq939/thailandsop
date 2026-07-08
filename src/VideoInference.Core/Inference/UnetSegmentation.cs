using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace VideoInferenceDemo;

public sealed class UnetSegmentationMetadata
{
    public float ProbabilityThreshold { get; init; } = 0.6f;
    public int MinComponentArea { get; init; } = 20;
    public float MinComponentPerimeter { get; init; }
    public float MinAreaPerimeterRatio { get; init; }
    public float MaxAreaPerimeterRatio { get; init; }
    public string? TensorRtCacheKey { get; init; }

    public static UnetSegmentationMetadata Default { get; } = new();
}

public sealed record UnetDefectComponent(
    int Index,
    int X,
    int Y,
    int Width,
    int Height,
    int AreaPx,
    float PerimeterPx,
    float AreaPerimeterRatio,
    float MeanProbability,
    float MaxProbability);

public sealed class UnetSegmentationResult
{
    public UnetSegmentationResult(
        IReadOnlyList<UnetDefectComponent> components,
        int rawComponentCount,
        byte[] mask,
        int maskWidth,
        int maskHeight,
        UnetSegmentationMetadata metadata)
    {
        Components = components ?? Array.Empty<UnetDefectComponent>();
        RawComponentCount = Math.Max(0, rawComponentCount);
        Mask = mask ?? Array.Empty<byte>();
        MaskWidth = Math.Max(0, maskWidth);
        MaskHeight = Math.Max(0, maskHeight);
        Metadata = metadata ?? UnetSegmentationMetadata.Default;
    }

    public IReadOnlyList<UnetDefectComponent> Components { get; }
    public int RawComponentCount { get; }
    public byte[] Mask { get; }
    public int MaskWidth { get; }
    public int MaskHeight { get; }
    public UnetSegmentationMetadata Metadata { get; }
    public int AcceptedComponentCount => Components.Count;
    public bool HasDefect => Components.Count > 0;
    public float MaxAreaPx => Components.Count == 0 ? 0f : Components.Max(item => item.AreaPx);
    public float MaxPerimeterPx => Components.Count == 0 ? 0f : Components.Max(item => item.PerimeterPx);
    public float MaxAreaPerimeterRatio => Components.Count == 0 ? 0f : Components.Max(item => item.AreaPerimeterRatio);
    public float MaxProbability => Components.Count == 0 ? 0f : Components.Max(item => item.MaxProbability);

    public string SummaryText => string.Join("; ", new[]
    {
        $"threshold={Metadata.ProbabilityThreshold.ToString("0.###", CultureInfo.InvariantCulture)}",
        $"accepted={AcceptedComponentCount.ToString(CultureInfo.InvariantCulture)}",
        $"raw={RawComponentCount.ToString(CultureInfo.InvariantCulture)}",
        $"maxArea={MaxAreaPx.ToString("0.#", CultureInfo.InvariantCulture)}",
        $"maxPerimeter={MaxPerimeterPx.ToString("0.#", CultureInfo.InvariantCulture)}",
        $"maxRatio={MaxAreaPerimeterRatio.ToString("0.###", CultureInfo.InvariantCulture)}",
        $"decision={(HasDefect ? "NG" : "OK")}"
    });

    public string ComponentsText => Components.Count == 0
        ? string.Empty
        : string.Join(Environment.NewLine, Components.Select(component =>
            $"#{component.Index.ToString(CultureInfo.InvariantCulture)} " +
            $"area={component.AreaPx.ToString(CultureInfo.InvariantCulture)} " +
            $"perimeter={component.PerimeterPx.ToString("0.#", CultureInfo.InvariantCulture)} " +
            $"ratio={component.AreaPerimeterRatio.ToString("0.###", CultureInfo.InvariantCulture)} " +
            $"bbox=({component.X.ToString(CultureInfo.InvariantCulture)},{component.Y.ToString(CultureInfo.InvariantCulture)},{component.Width.ToString(CultureInfo.InvariantCulture)},{component.Height.ToString(CultureInfo.InvariantCulture)}) " +
            $"meanProb={component.MeanProbability.ToString("0.###", CultureInfo.InvariantCulture)} " +
            $"maxProb={component.MaxProbability.ToString("0.###", CultureInfo.InvariantCulture)}"));
}

public sealed class UnetSegmentationDetector : IDisposable
{
    private readonly string _modelPath;
    private readonly UnetSegmentationMetadata _metadata;
    private readonly OrtModelRuntime _runtime;
    private readonly UnetSegmentationPreprocessor _preprocessor;
    private readonly UnetSegmentationPostprocessor _postprocessor;

    public UnetSegmentationDetector(
        string modelPath,
        InferenceDeviceKind deviceKind,
        UnetSegmentationMetadata? metadata)
    {
        _modelPath = modelPath;
        _metadata = metadata ?? UnetSegmentationMetadata.Default;
        _runtime = new OrtModelRuntime(_modelPath, BuildRuntimeOptions(deviceKind), preferredInputName: "input", preferredOutputName: "logits");
        ResolveInputSize(_runtime.InputDimensions, out var inputWidth, out var inputHeight);
        _preprocessor = new UnetSegmentationPreprocessor(_runtime.InputName, inputWidth, inputHeight);
        _postprocessor = new UnetSegmentationPostprocessor(_runtime.OutputName, _metadata);
        ActiveDeviceLabel = _runtime.ActiveDeviceLabel ?? string.Empty;
    }

    public string ActiveDeviceLabel { get; }
    public YoloInferenceTiming LastTiming { get; private set; } = YoloInferenceTiming.Empty;

    public UnetSegmentationResult Predict(Mat image)
    {
        if (image == null || image.Empty())
        {
            return new UnetSegmentationResult(Array.Empty<UnetDefectComponent>(), 0, Array.Empty<byte>(), 0, 0, _metadata);
        }

        var totalWatch = Stopwatch.StartNew();
        var preprocessWatch = Stopwatch.StartNew();
        var input = _preprocessor.Preprocess(image, out var context);
        preprocessWatch.Stop();

        var runWatch = Stopwatch.StartNew();
        using var output = _runtime.Run(input);
        runWatch.Stop();

        var postprocessWatch = Stopwatch.StartNew();
        var result = _postprocessor.Process(output, context);
        postprocessWatch.Stop();
        totalWatch.Stop();

        LastTiming = new YoloInferenceTiming(
            preprocessWatch.Elapsed.TotalMilliseconds,
            runWatch.Elapsed.TotalMilliseconds,
            postprocessWatch.Elapsed.TotalMilliseconds,
            totalWatch.Elapsed.TotalMilliseconds);
        return result;
    }

    public void Dispose()
    {
        _runtime.Dispose();
    }

    private static OrtSessionFactoryOptions BuildRuntimeOptions(InferenceDeviceKind deviceKind)
    {
        return deviceKind switch
        {
            InferenceDeviceKind.Cpu => new OrtSessionFactoryOptions
            {
                DeviceKind = deviceKind,
                ProviderOrder = new[] { OrtExecutionProviderKind.Cpu }
            },
            InferenceDeviceKind.GpuCuda => new OrtSessionFactoryOptions
            {
                DeviceKind = deviceKind,
                ProviderOrder = new[] { OrtExecutionProviderKind.Cuda, OrtExecutionProviderKind.Cpu }
            },
            _ => new OrtSessionFactoryOptions
            {
                DeviceKind = deviceKind,
                ProviderOrder = new[] { OrtExecutionProviderKind.TensorRt, OrtExecutionProviderKind.Cuda, OrtExecutionProviderKind.Cpu },
                TensorRtFp16 = true,
                TensorRtEngineCache = true
            }
        };
    }

    private static void ResolveInputSize(IReadOnlyList<int> dimensions, out int width, out int height)
    {
        width = 640;
        height = 640;
        if (dimensions.Count >= 4)
        {
            if (dimensions[^1] > 0)
            {
                width = dimensions[^1];
            }

            if (dimensions[^2] > 0)
            {
                height = dimensions[^2];
            }
        }
    }
}

public sealed class UnetSegmentationPreprocessor : IModelPreprocessor<Mat, UnetImageTransformContext>
{
    private static readonly Scalar Mean = new(0.485, 0.456, 0.406);
    private static readonly Scalar Std = new(0.229, 0.224, 0.225);
    private readonly string _inputName;
    private readonly int _inputWidth;
    private readonly int _inputHeight;

    public UnetSegmentationPreprocessor(string inputName, int inputWidth, int inputHeight)
    {
        _inputName = inputName;
        _inputWidth = Math.Max(1, inputWidth);
        _inputHeight = Math.Max(1, inputHeight);
    }

    public ModelInput Preprocess(Mat image, out UnetImageTransformContext context)
    {
        using var rgb = ConvertToRgb(image);
        using var resized = new Mat();
        Cv2.Resize(rgb, resized, new Size(_inputWidth, _inputHeight), 0, 0, InterpolationFlags.Area);

        using var normalized = new Mat();
        resized.ConvertTo(normalized, MatType.CV_32FC3, 1.0 / 255.0);
        Cv2.Subtract(normalized, Mean, normalized);
        Cv2.Divide(normalized, Std, normalized);

        using var blob = CvDnn.BlobFromImage(
            normalized,
            1.0,
            new Size(_inputWidth, _inputHeight),
            Scalar.All(0),
            swapRB: false,
            crop: false);
        using var continuousClone = blob.IsContinuous() ? null : blob.Clone();
        var continuousBlob = continuousClone ?? blob;
        var elementCount = checked((int)continuousBlob.Total());
        var buffer = new float[elementCount];
        Marshal.Copy(continuousBlob.Data, buffer, 0, elementCount);

        context = new UnetImageTransformContext(image.Width, image.Height);
        var tensor = new DenseTensor<float>(buffer, new[] { 1, 3, _inputHeight, _inputWidth });
        return new ModelInput(new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) });
    }

    private static Mat ConvertToRgb(Mat image)
    {
        var rgb = new Mat();
        var code = image.Channels() switch
        {
            1 => ColorConversionCodes.GRAY2RGB,
            4 => ColorConversionCodes.BGRA2RGB,
            _ => ColorConversionCodes.BGR2RGB
        };
        Cv2.CvtColor(image, rgb, code);
        return rgb;
    }
}

public readonly record struct UnetImageTransformContext(int OriginalWidth, int OriginalHeight);

public sealed class UnetSegmentationPostprocessor : IModelPostprocessor<UnetImageTransformContext, UnetSegmentationResult>
{
    private readonly string _outputName;
    private readonly UnetSegmentationMetadata _metadata;

    public UnetSegmentationPostprocessor(string outputName, UnetSegmentationMetadata? metadata)
    {
        _outputName = outputName;
        _metadata = metadata ?? UnetSegmentationMetadata.Default;
    }

    public UnetSegmentationResult Process(ModelOutput output, UnetImageTransformContext context)
    {
        var value = output.Outputs.FirstOrDefault(item => string.Equals(item.Name, _outputName, StringComparison.OrdinalIgnoreCase))
                    ?? output.Outputs.FirstOrDefault();
        return value?.Value is Tensor<float> logits
            ? Postprocess(logits, context)
            : new UnetSegmentationResult(Array.Empty<UnetDefectComponent>(), 0, Array.Empty<byte>(), context.OriginalWidth, context.OriginalHeight, _metadata);
    }

    public UnetSegmentationResult Postprocess(Tensor<float> logits, UnetImageTransformContext context)
    {
        ArgumentNullException.ThrowIfNull(logits);
        var dims = logits.Dimensions.ToArray();
        if (dims.Length != 4 || dims[0] <= 0 || dims[1] <= 0 || dims[2] <= 0 || dims[3] <= 0)
        {
            return new UnetSegmentationResult(Array.Empty<UnetDefectComponent>(), 0, Array.Empty<byte>(), context.OriginalWidth, context.OriginalHeight, _metadata);
        }

        var modelHeight = dims[2];
        var modelWidth = dims[3];
        var smallProbabilities = new float[modelWidth * modelHeight];
        for (var y = 0; y < modelHeight; y++)
        {
            var rowOffset = y * modelWidth;
            for (var x = 0; x < modelWidth; x++)
            {
                smallProbabilities[rowOffset + x] = Sigmoid(logits[0, 0, y, x]);
            }
        }

        var targetWidth = Math.Max(1, context.OriginalWidth);
        var targetHeight = Math.Max(1, context.OriginalHeight);
        using var smallMat = Mat.FromPixelData(modelHeight, modelWidth, MatType.CV_32FC1, smallProbabilities);
        using var probabilityMat = new Mat();
        Cv2.Resize(smallMat, probabilityMat, new Size(targetWidth, targetHeight), 0, 0, InterpolationFlags.Linear);

        using var probabilityContinuous = probabilityMat.IsContinuous() ? null : probabilityMat.Clone();
        var probabilitySource = probabilityContinuous ?? probabilityMat;
        var probabilityCount = checked(targetWidth * targetHeight);
        var probabilities = new float[probabilityCount];
        Marshal.Copy(probabilitySource.Data, probabilities, 0, probabilityCount);

        var mask = new byte[probabilityCount];
        var threshold = _metadata.ProbabilityThreshold;
        for (var i = 0; i < probabilities.Length; i++)
        {
            if (probabilities[i] >= threshold)
            {
                mask[i] = 255;
            }
        }

        using var maskMat = Mat.FromPixelData(targetHeight, targetWidth, MatType.CV_8UC1, mask);
        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        var labelCount = Cv2.ConnectedComponentsWithStats(maskMat, labels, stats, centroids, PixelConnectivity.Connectivity8, MatType.CV_32S);
        var rawComponentCount = Math.Max(0, labelCount - 1);
        if (rawComponentCount == 0)
        {
            return new UnetSegmentationResult(Array.Empty<UnetDefectComponent>(), 0, mask, targetWidth, targetHeight, _metadata);
        }

        using var labelsContinuous = labels.IsContinuous() ? null : labels.Clone();
        var labelSource = labelsContinuous ?? labels;
        var labelValues = new int[probabilityCount];
        Marshal.Copy(labelSource.Data, labelValues, 0, probabilityCount);

        var accepted = new List<UnetDefectComponent>();
        var acceptedMask = new byte[probabilityCount];
        for (var label = 1; label < labelCount; label++)
        {
            var area = stats.At<int>(label, 4);
            var x = stats.At<int>(label, 0);
            var y = stats.At<int>(label, 1);
            var width = stats.At<int>(label, 2);
            var height = stats.At<int>(label, 3);
            if (area <= 0)
            {
                continue;
            }

            var componentMask = new byte[probabilityCount];
            var probabilitySum = 0.0;
            var maxProbability = 0f;
            for (var i = 0; i < labelValues.Length; i++)
            {
                if (labelValues[i] != label)
                {
                    continue;
                }

                componentMask[i] = 255;
                probabilitySum += probabilities[i];
                if (probabilities[i] > maxProbability)
                {
                    maxProbability = probabilities[i];
                }
            }

            var perimeter = ComputePerimeter(componentMask, targetWidth, targetHeight);
            var ratio = perimeter > 0f ? area / perimeter : 0f;
            if (!PassesFilters(area, perimeter, ratio))
            {
                continue;
            }

            for (var i = 0; i < componentMask.Length; i++)
            {
                if (componentMask[i] != 0)
                {
                    acceptedMask[i] = 255;
                }
            }

            accepted.Add(new UnetDefectComponent(
                accepted.Count + 1,
                x,
                y,
                width,
                height,
                area,
                perimeter,
                ratio,
                (float)(probabilitySum / Math.Max(1, area)),
                maxProbability));
        }

        return new UnetSegmentationResult(accepted, rawComponentCount, acceptedMask, targetWidth, targetHeight, _metadata);
    }

    private bool PassesFilters(int area, float perimeter, float ratio)
    {
        if (area < Math.Max(0, _metadata.MinComponentArea))
        {
            return false;
        }

        if (_metadata.MinComponentPerimeter > 0f && perimeter < _metadata.MinComponentPerimeter)
        {
            return false;
        }

        if (_metadata.MinAreaPerimeterRatio > 0f && ratio < _metadata.MinAreaPerimeterRatio)
        {
            return false;
        }

        if (_metadata.MaxAreaPerimeterRatio > 0f && ratio > _metadata.MaxAreaPerimeterRatio)
        {
            return false;
        }

        return true;
    }

    private static float ComputePerimeter(byte[] componentMask, int width, int height)
    {
        using var componentMat = Mat.FromPixelData(height, width, MatType.CV_8UC1, componentMask);
        Cv2.FindContours(componentMat, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxNone);
        var perimeter = 0.0;
        foreach (var contour in contours)
        {
            perimeter += Cv2.ArcLength(contour, closed: true);
        }

        return (float)perimeter;
    }

    private static float Sigmoid(float value)
    {
        if (value >= 0f)
        {
            var z = MathF.Exp(-value);
            return 1f / (1f + z);
        }
        else
        {
            var z = MathF.Exp(value);
            return z / (1f + z);
        }
    }
}
