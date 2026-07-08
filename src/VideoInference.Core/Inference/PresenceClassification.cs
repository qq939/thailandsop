using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace VideoInferenceDemo;

public sealed class PresenceClassificationMetadata
{
    public string PresentClass { get; init; } = "OK";
    public string AbsentClass { get; init; } = "NG";
    public float ProbabilityThreshold { get; init; } = 0.5f;
    public string? TensorRtCacheKey { get; init; }

    public static PresenceClassificationMetadata Default { get; } = new();
}

public sealed record PresenceClassificationScore(
    int ClassId,
    string ClassName,
    float Probability,
    float Logit);

public sealed class PresenceClassificationResult
{
    public PresenceClassificationResult(
        IReadOnlyList<PresenceClassificationScore> scores,
        PresenceClassificationMetadata metadata)
    {
        Scores = scores ?? Array.Empty<PresenceClassificationScore>();
        Metadata = metadata ?? PresenceClassificationMetadata.Default;
        Predicted = Scores
            .OrderByDescending(item => item.Probability)
            .FirstOrDefault();
        Present = ResolveScore(Metadata.PresentClass, fallbackIndex: 0);
        Absent = ResolveScore(Metadata.AbsentClass, fallbackIndex: Scores.Count > 1 ? 1 : 0);
    }

    public IReadOnlyList<PresenceClassificationScore> Scores { get; }
    public PresenceClassificationMetadata Metadata { get; }
    public PresenceClassificationScore? Predicted { get; }
    public PresenceClassificationScore? Present { get; }
    public PresenceClassificationScore? Absent { get; }
    public float PresentProbability => Present?.Probability ?? 0f;
    public float AbsentProbability => Absent?.Probability ?? 0f;
    public bool IsAbsent => AbsentProbability >= Metadata.ProbabilityThreshold;
    public bool IsPresent => !IsAbsent;
    public string DecisionText => IsAbsent ? "无产品" : "有产品";

    public string SummaryText =>
        $"产品有无：{DecisionText}，概率={(IsAbsent ? AbsentProbability : PresentProbability).ToString("0.###", CultureInfo.InvariantCulture)}";

    private PresenceClassificationScore? ResolveScore(string className, int fallbackIndex)
    {
        var match = Scores.FirstOrDefault(item =>
            string.Equals(item.ClassName, className, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            return match;
        }

        return fallbackIndex >= 0 && fallbackIndex < Scores.Count
            ? Scores[fallbackIndex]
            : Scores.FirstOrDefault();
    }
}

public sealed class PresenceClassificationPayload : VisionTaskPayload
{
    public PresenceClassificationPayload(PresenceClassificationResult result)
    {
        Result = result ?? new PresenceClassificationResult(
            Array.Empty<PresenceClassificationScore>(),
            PresenceClassificationMetadata.Default);
    }

    public PresenceClassificationResult Result { get; }
}

public sealed class PresenceClassificationDetector : IDisposable
{
    private readonly string _modelPath;
    private readonly PresenceClassificationMetadata _metadata;
    private readonly string[] _classNames;
    private readonly OrtModelRuntime _runtime;
    private readonly PresenceClassificationPreprocessor _preprocessor;
    private readonly PresenceClassificationPostprocessor _postprocessor;

    public PresenceClassificationDetector(
        string modelPath,
        InferenceDeviceKind deviceKind,
        string[]? classNames,
        PresenceClassificationMetadata? metadata)
    {
        _modelPath = modelPath;
        _metadata = metadata ?? PresenceClassificationMetadata.Default;
        _classNames = classNames is { Length: > 0 }
            ? classNames
            : new[] { _metadata.PresentClass, _metadata.AbsentClass };
        _runtime = new OrtModelRuntime(_modelPath, BuildRuntimeOptions(deviceKind), preferredInputName: "input", preferredOutputName: "logits");
        ResolveInputSize(_runtime.InputDimensions, out var inputWidth, out var inputHeight);
        _preprocessor = new PresenceClassificationPreprocessor(_runtime.InputName, inputWidth, inputHeight);
        _postprocessor = new PresenceClassificationPostprocessor(_runtime.OutputName, _metadata, _classNames);
        ActiveDeviceLabel = _runtime.ActiveDeviceLabel ?? string.Empty;
    }

    public string ActiveDeviceLabel { get; }
    public YoloInferenceTiming LastTiming { get; private set; } = YoloInferenceTiming.Empty;

    public PresenceClassificationResult Predict(Mat image)
    {
        if (image == null || image.Empty())
        {
            return new PresenceClassificationResult(Array.Empty<PresenceClassificationScore>(), _metadata);
        }

        var totalWatch = Stopwatch.StartNew();
        var preprocessWatch = Stopwatch.StartNew();
        var input = _preprocessor.Preprocess(image, out _);
        preprocessWatch.Stop();

        var runWatch = Stopwatch.StartNew();
        using var output = _runtime.Run(input);
        runWatch.Stop();

        var postprocessWatch = Stopwatch.StartNew();
        var result = _postprocessor.Process(output, default);
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
        width = 224;
        height = 224;
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

public sealed class PresenceClassificationPreprocessor : IModelPreprocessor<Mat, PresenceClassificationImageTransformContext>
{
    private static readonly Scalar Mean = new(0.485, 0.456, 0.406);
    private static readonly Scalar Std = new(0.229, 0.224, 0.225);
    private readonly string _inputName;
    private readonly int _inputWidth;
    private readonly int _inputHeight;

    public PresenceClassificationPreprocessor(string inputName, int inputWidth, int inputHeight)
    {
        _inputName = inputName;
        _inputWidth = Math.Max(1, inputWidth);
        _inputHeight = Math.Max(1, inputHeight);
    }

    public ModelInput Preprocess(Mat image, out PresenceClassificationImageTransformContext context)
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

        context = new PresenceClassificationImageTransformContext(image.Width, image.Height);
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

public readonly record struct PresenceClassificationImageTransformContext(int OriginalWidth, int OriginalHeight);

public sealed class PresenceClassificationPostprocessor : IModelPostprocessor<PresenceClassificationImageTransformContext, PresenceClassificationResult>
{
    private readonly string _outputName;
    private readonly PresenceClassificationMetadata _metadata;
    private readonly string[] _classNames;

    public PresenceClassificationPostprocessor(
        string outputName,
        PresenceClassificationMetadata? metadata,
        string[]? classNames)
    {
        _outputName = outputName;
        _metadata = metadata ?? PresenceClassificationMetadata.Default;
        _classNames = classNames is { Length: > 0 }
            ? classNames
            : new[] { _metadata.PresentClass, _metadata.AbsentClass };
    }

    public PresenceClassificationResult Process(ModelOutput output, PresenceClassificationImageTransformContext context)
    {
        var value = output.Outputs.FirstOrDefault(item => string.Equals(item.Name, _outputName, StringComparison.OrdinalIgnoreCase))
                    ?? output.Outputs.FirstOrDefault();
        return value?.Value is Tensor<float> logits
            ? Postprocess(logits)
            : new PresenceClassificationResult(Array.Empty<PresenceClassificationScore>(), _metadata);
    }

    public PresenceClassificationResult Postprocess(Tensor<float> logits)
    {
        ArgumentNullException.ThrowIfNull(logits);
        var values = ReadFirstBatch(logits);
        if (values.Length == 0)
        {
            return new PresenceClassificationResult(Array.Empty<PresenceClassificationScore>(), _metadata);
        }

        var probabilities = Softmax(values);
        var scores = probabilities
            .Select((probability, index) => new PresenceClassificationScore(
                index,
                index < _classNames.Length && !string.IsNullOrWhiteSpace(_classNames[index])
                    ? _classNames[index]
                    : $"class-{index.ToString(CultureInfo.InvariantCulture)}",
                probability,
                values[index]))
            .ToArray();
        return new PresenceClassificationResult(scores, _metadata);
    }

    private static float[] ReadFirstBatch(Tensor<float> logits)
    {
        var dims = logits.Dimensions.ToArray();
        if (dims.Length == 1)
        {
            var values = new float[dims[0]];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = logits[i];
            }

            return values;
        }

        if (dims.Length < 2 || dims[0] <= 0 || dims[1] <= 0)
        {
            return Array.Empty<float>();
        }

        var classCount = dims[1];
        var output = new float[classCount];
        for (var i = 0; i < classCount; i++)
        {
            output[i] = logits[0, i];
        }

        return output;
    }

    private static float[] Softmax(float[] logits)
    {
        if (logits.Length == 0)
        {
            return Array.Empty<float>();
        }

        var max = logits.Max();
        var exps = new double[logits.Length];
        double sum = 0;
        for (var i = 0; i < logits.Length; i++)
        {
            var value = Math.Exp(logits[i] - max);
            exps[i] = value;
            sum += value;
        }

        if (sum <= 0)
        {
            return logits.Select(_ => 0f).ToArray();
        }

        return exps.Select(value => (float)(value / sum)).ToArray();
    }
}
