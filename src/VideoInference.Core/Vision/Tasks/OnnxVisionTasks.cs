using OpenCvSharp;

namespace VideoInferenceDemo;

internal sealed class YoloVisionTask : IVisionTask
{
    private readonly Yolo11Detector _detector;

    public YoloVisionTask(
        string taskId,
        string modelPath,
        InferenceDeviceKind deviceKind,
        float confidenceThreshold,
        float nmsThreshold,
        string[]? classNames,
        YoloDetectionMetadata? metadata)
    {
        TaskId = string.IsNullOrWhiteSpace(taskId) ? modelPath : taskId;
        _detector = new Yolo11Detector(modelPath, deviceKind, confidenceThreshold, nmsThreshold, classNames, metadata);
    }

    public string TaskId { get; }
    public VisionTaskKind TaskKind => VisionTaskKind.Detection;
    public VisionRuntimeKind RuntimeKind => VisionRuntimeKind.OnnxRuntime;
    public string? ActiveDeviceLabel => _detector.ActiveDeviceLabel;

    public VisionTaskExecutionResult Execute(Mat image, VisionTaskExecutionContext context)
    {
        var results = _detector.Predict(image);
        var detections = PipelineDetectionEntityBuilder.FromYolo(results, image.Width, image.Height);
        return new VisionTaskExecutionResult
        {
            Payload = new DetectionPayload(detections),
            Annotate = frame => PipelineFrameAnnotator.DrawDetections(frame, results, context.RenderStyle.ToPipelineDrawStyle()),
            DeviceLabel = _detector.ActiveDeviceLabel,
            Metrics = YoloTimingMetricBuilder.BuildYoloTimingMetrics(_detector.LastTiming)
        };
    }

    public void Warmup(int width, int height)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC3, Scalar.All(0));
        _ = _detector.Predict(mat);
    }

    public void UpdateClassNames(string[]? classNames)
    {
        _detector.UpdateClasses(classNames);
    }

    public bool TryHandleFailure(Exception ex, out string message)
    {
        message = $"Inference failed: {ex.Message}";
        return false;
    }

    public void Dispose()
    {
        _detector.Dispose();
    }
}

internal sealed class YoloSegmentationVisionTask : IVisionTask
{
    private readonly YoloSegmentationDetector _detector;

    public YoloSegmentationVisionTask(
        string taskId,
        string modelPath,
        InferenceDeviceKind deviceKind,
        float confidenceThreshold,
        float nmsThreshold,
        string[]? classNames,
        YoloSegmentationMetadata? metadata)
    {
        TaskId = string.IsNullOrWhiteSpace(taskId) ? modelPath : taskId;
        _detector = new YoloSegmentationDetector(modelPath, deviceKind, confidenceThreshold, nmsThreshold, classNames, metadata);
    }

    public string TaskId { get; }
    public VisionTaskKind TaskKind => VisionTaskKind.Segmentation;
    public VisionRuntimeKind RuntimeKind => VisionRuntimeKind.OnnxRuntime;
    public string? ActiveDeviceLabel => _detector.ActiveDeviceLabel;

    public VisionTaskExecutionResult Execute(Mat image, VisionTaskExecutionContext context)
    {
        var results = _detector.Predict(image);
        var detections = PipelineDetectionEntityBuilder.FromYoloSegmentation(results, image.Width, image.Height);
        return new VisionTaskExecutionResult
        {
            Payload = new SegmentationPayload(results, detections),
            Annotate = frame => PipelineFrameAnnotator.DrawSegmentations(frame, results, context.RenderStyle.ToPipelineDrawStyle()),
            DeviceLabel = _detector.ActiveDeviceLabel,
            Metrics = YoloTimingMetricBuilder.BuildYoloTimingMetrics(_detector.LastTiming)
        };
    }

    public void Warmup(int width, int height)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC3, Scalar.All(0));
        _ = _detector.Predict(mat);
    }

    public void UpdateClassNames(string[]? classNames)
    {
        _detector.UpdateClasses(classNames);
    }

    public bool TryHandleFailure(Exception ex, out string message)
    {
        message = $"Inference failed: {ex.Message}";
        return false;
    }

    public void Dispose()
    {
        _detector.Dispose();
    }
}

internal sealed class UnetSegmentationVisionTask : IVisionTask
{
    private readonly UnetSegmentationDetector _detector;
    private readonly string _className;

    public UnetSegmentationVisionTask(
        string taskId,
        string modelPath,
        InferenceDeviceKind deviceKind,
        string[]? classNames,
        UnetSegmentationMetadata? metadata)
    {
        TaskId = string.IsNullOrWhiteSpace(taskId) ? modelPath : taskId;
        _className = classNames is { Length: > 0 } && !string.IsNullOrWhiteSpace(classNames[0])
            ? classNames[0]
            : "scratch";
        _detector = new UnetSegmentationDetector(modelPath, deviceKind, metadata);
    }

    public string TaskId { get; }
    public VisionTaskKind TaskKind => VisionTaskKind.UnetSegmentation;
    public VisionRuntimeKind RuntimeKind => VisionRuntimeKind.OnnxRuntime;
    public string? ActiveDeviceLabel => _detector.ActiveDeviceLabel;

    public VisionTaskExecutionResult Execute(Mat image, VisionTaskExecutionContext context)
    {
        var result = _detector.Predict(image);
        var detections = PipelineDetectionEntityBuilder.FromUnetSegmentation(result, _className);
        return new VisionTaskExecutionResult
        {
            Payload = new UnetSegmentationPayload(result, detections),
            Annotate = frame => PipelineFrameAnnotator.DrawUnetSegmentation(frame, result, 0, _className, context.RenderStyle.ToPipelineDrawStyle()),
            DeviceLabel = _detector.ActiveDeviceLabel,
            Metrics = YoloTimingMetricBuilder.BuildYoloTimingMetrics(_detector.LastTiming)
        };
    }

    public void Warmup(int width, int height)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC3, Scalar.All(0));
        _ = _detector.Predict(mat);
    }

    public void UpdateClassNames(string[]? classNames)
    {
    }

    public bool TryHandleFailure(Exception ex, out string message)
    {
        message = $"Inference failed: {ex.Message}";
        return false;
    }

    public void Dispose()
    {
        _detector.Dispose();
    }
}

internal sealed class PresenceClassificationVisionTask : IVisionTask
{
    private readonly PresenceClassificationDetector _detector;

    public PresenceClassificationVisionTask(
        string taskId,
        string modelPath,
        InferenceDeviceKind deviceKind,
        string[]? classNames,
        PresenceClassificationMetadata? metadata)
    {
        TaskId = string.IsNullOrWhiteSpace(taskId) ? modelPath : taskId;
        _detector = new PresenceClassificationDetector(modelPath, deviceKind, classNames, metadata);
    }

    public string TaskId { get; }
    public VisionTaskKind TaskKind => VisionTaskKind.PresenceClassification;
    public VisionRuntimeKind RuntimeKind => VisionRuntimeKind.OnnxRuntime;
    public string? ActiveDeviceLabel => _detector.ActiveDeviceLabel;

    public VisionTaskExecutionResult Execute(Mat image, VisionTaskExecutionContext context)
    {
        var result = _detector.Predict(image);
        return new VisionTaskExecutionResult
        {
            Payload = new PresenceClassificationPayload(result),
            Annotate = _ => { },
            DeviceLabel = _detector.ActiveDeviceLabel,
            Metrics = YoloTimingMetricBuilder.BuildYoloTimingMetrics(_detector.LastTiming)
        };
    }

    public void Warmup(int width, int height)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC3, Scalar.All(0));
        _ = _detector.Predict(mat);
    }

    public void UpdateClassNames(string[]? classNames)
    {
    }

    public bool TryHandleFailure(Exception ex, out string message)
    {
        message = $"Inference failed: {ex.Message}";
        return false;
    }

    public void Dispose()
    {
        _detector.Dispose();
    }
}

internal sealed class YoloObbVisionTask : IVisionTask
{
    private readonly YoloObbDetector _detector;

    public YoloObbVisionTask(
        string taskId,
        string modelPath,
        InferenceDeviceKind deviceKind,
        float confidenceThreshold,
        float nmsThreshold,
        string[]? classNames,
        YoloObbDetectionMetadata? metadata)
    {
        TaskId = string.IsNullOrWhiteSpace(taskId) ? modelPath : taskId;
        _detector = new YoloObbDetector(modelPath, deviceKind, confidenceThreshold, nmsThreshold, classNames, metadata);
    }

    public string TaskId { get; }
    public VisionTaskKind TaskKind => VisionTaskKind.ObbDetection;
    public VisionRuntimeKind RuntimeKind => VisionRuntimeKind.OnnxRuntime;
    public string? ActiveDeviceLabel => _detector.ActiveDeviceLabel;

    public VisionTaskExecutionResult Execute(Mat image, VisionTaskExecutionContext context)
    {
        var results = _detector.Predict(image);
        var metrics = new Dictionary<string, string>(YoloTimingMetricBuilder.BuildYoloTimingMetrics(_detector.LastTiming), StringComparer.OrdinalIgnoreCase)
        {
            ["locator.minScore"] = _detector.LocatorMinScore.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        return new VisionTaskExecutionResult
        {
            Payload = new ObbDetectionPayload(results),
            Annotate = frame => { },
            DeviceLabel = _detector.ActiveDeviceLabel,
            Metrics = metrics
        };
    }

    public void Warmup(int width, int height)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC3, Scalar.All(0));
        _ = _detector.Predict(mat);
    }

    public void UpdateClassNames(string[]? classNames)
    {
        _detector.UpdateClasses(classNames);
    }

    public bool TryHandleFailure(Exception ex, out string message)
    {
        message = $"Inference failed: {ex.Message}";
        return false;
    }

    public void Dispose()
    {
        _detector.Dispose();
    }
}

internal static class YoloTimingMetricBuilder
{
    public static IReadOnlyDictionary<string, string> BuildYoloTimingMetrics(YoloInferenceTiming timing)
    {
        return new Dictionary<string, string>
        {
            ["preprocessMs"] = timing.PreprocessMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            ["ortRunMs"] = timing.RunMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            ["postprocessMs"] = timing.PostprocessMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            ["modelTotalMs"] = timing.TotalMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
        };
    }
}

internal sealed class SequenceVisionTask : IVisionTask
{
    private readonly SequenceOnnxModel _sequenceModel;

    public SequenceVisionTask(string taskId, string modelPath, InferenceDeviceKind deviceKind, SequenceModelMetadata metadata)
    {
        TaskId = string.IsNullOrWhiteSpace(taskId) ? modelPath : taskId;
        _sequenceModel = new SequenceOnnxModel(modelPath, deviceKind, metadata);
    }

    public string TaskId { get; }
    public VisionTaskKind TaskKind => VisionTaskKind.SequenceBands;
    public VisionRuntimeKind RuntimeKind => VisionRuntimeKind.OnnxRuntime;
    public string? ActiveDeviceLabel => _sequenceModel.ActiveDeviceLabel;

    public VisionTaskExecutionResult Execute(Mat image, VisionTaskExecutionContext context)
    {
        var bands = _sequenceModel.Predict(image);
        var detections = PipelineDetectionEntityBuilder.FromSequence(bands);
        return new VisionTaskExecutionResult
        {
            Payload = new SequenceBandsPayload(bands, detections),
            Annotate = frame => PipelineFrameAnnotator.DrawSequenceBands(frame, bands, context.RenderStyle.ToPipelineDrawStyle()),
            DeviceLabel = _sequenceModel.ActiveDeviceLabel
        };
    }

    public void Warmup(int width, int height)
    {
        _sequenceModel.Warmup(width, height);
    }

    public void UpdateClassNames(string[]? classNames)
    {
    }

    public bool TryHandleFailure(Exception ex, out string message)
    {
        message = $"Inference failed: {ex.Message}";
        return false;
    }

    public void Dispose()
    {
        _sequenceModel.Dispose();
    }
}

internal static class PipelineDetectionEntityBuilder
{
    public static List<DetectionEntity> FromYolo(IReadOnlyList<YoloDetection> results, int width, int height)
    {
        var detections = new List<DetectionEntity>(results.Count);
        foreach (var det in results)
        {
            if (TryCreate(det, width, height, out var entity))
            {
                detections.Add(entity);
            }
        }

        return detections;
    }

    public static List<DetectionEntity> FromYoloSegmentation(IReadOnlyList<YoloSegmentation> results, int width, int height)
    {
        var detections = new List<DetectionEntity>(results.Count);
        foreach (var det in results)
        {
            detections.Add(new DetectionEntity
            {
                ClassId = det.ClassId,
                ClassName = det.ClassName,
                Score = det.Score,
                X1 = ClampToFloat(det.X1, 0, width),
                Y1 = ClampToFloat(det.Y1, 0, height),
                X2 = ClampToFloat(det.X2, 0, width),
                Y2 = ClampToFloat(det.Y2, 0, height)
            });
        }

        return detections;
    }

    public static List<DetectionEntity> FromUnetSegmentation(UnetSegmentationResult result, string className)
    {
        if (result.Components.Count == 0)
        {
            return [];
        }

        return result.Components
            .Select(component => new DetectionEntity
            {
                ClassId = 0,
                ClassName = string.IsNullOrWhiteSpace(className) ? "scratch" : className,
                Score = component.MaxProbability,
                X1 = component.X,
                Y1 = component.Y,
                X2 = component.X + component.Width,
                Y2 = component.Y + component.Height
            })
            .ToList();
    }

    public static List<DetectionEntity> FromSequence(IReadOnlyList<SequenceBandPrediction> bands)
    {
        var detections = new List<DetectionEntity>(bands.Count);
        foreach (var band in bands)
        {
            detections.Add(new DetectionEntity
            {
                ClassId = band.ClassId,
                ClassName = band.ClassName,
                Score = band.Confidence,
                X1 = band.X0,
                Y1 = band.Y0,
                X2 = band.X1,
                Y2 = band.Y1
            });
        }

        return detections;
    }

    private static bool TryCreate(YoloDetection det, int width, int height, out DetectionEntity entity)
    {
        entity = default!;
        var x1 = ClampToFloat(det.X1, 0, width);
        var y1 = ClampToFloat(det.Y1, 0, height);
        var x2 = ClampToFloat(det.X2, 0, width);
        var y2 = ClampToFloat(det.Y2, 0, height);

        entity = new DetectionEntity
        {
            ClassId = det.ClassId,
            ClassName = det.ClassName,
            Score = det.Score,
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2
        };
        return true;
    }

    private static float ClampToFloat(double value, float min, float max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return min;
        }

        var v = (float)value;
        if (v < min)
        {
            return min;
        }

        return v > max ? max : v;
    }
}

internal static class VisionTaskRenderStyleExtensions
{
    public static PipelineDrawStyle ToPipelineDrawStyle(this VisionTaskRenderStyle style)
    {
        return new PipelineDrawStyle(
            style.GlobalOverride,
            style.OverridesByClass,
            style.BoxThickness,
            style.LabelFontScale);
    }
}
