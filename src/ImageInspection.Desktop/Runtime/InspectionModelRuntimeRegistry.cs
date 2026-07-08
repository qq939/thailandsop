using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using OpenCvSharp;

namespace VideoInferenceDemo.ImageInspection.Runtime;

public interface IInspectionModelRuntime
{
    InspectionModelExecutionResult Execute(string modelId, Mat image);

    void Warmup(string modelId, int width = 640, int height = 640, int iterations = 5);
}

public sealed class InspectionModelRuntimeRegistry : IInspectionModelRuntime, IDisposable
{
    private readonly string _modelConfigPath;
    private readonly ConcurrentDictionary<string, Lazy<ModelRuntimeHandle>> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, LatencyWindow> _ortLatencyWindows = new(StringComparer.OrdinalIgnoreCase);

    public InspectionModelRuntimeRegistry(string modelConfigPath)
    {
        _modelConfigPath = modelConfigPath;
    }

    public InspectionModelExecutionResult Execute(string modelId, Mat image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(image);

        var handle = _models.GetOrAdd(modelId.Trim(), id => new Lazy<ModelRuntimeHandle>(() => CreateHandle(id))).Value;
        handle.SyncRoot.Wait();
        try
        {
            // ImageInspection 管线传入的图像已是裁剪后的 ROI，OCR 任务不再重复裁剪
            if (handle.Task is OcrVisionTask ocrTask)
            {
                ocrTask.UpdateRoi(0, 0, image.Width, image.Height);
            }

            var result = handle.Task.Execute(
                image,
                new VisionTaskExecutionContext(
                    new SessionFrameContext(
                        0,
                        0,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        0,
                        image.Width,
                        image.Height),
                    new VisionTaskRenderStyle(null, null, 2, 0.8)));
            var metrics = new Dictionary<string, string>(result.Metrics, StringComparer.OrdinalIgnoreCase);
            if (metrics.TryGetValue("ortRunMs", out var ortRunMsText) &&
                double.TryParse(
                    ortRunMsText,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var ortRunMs))
            {
                var stats = _ortLatencyWindows.GetOrAdd(handle.Config.Id, _ => new LatencyWindow(20)).Add(ortRunMs);
                metrics["ortAvgMs"] = stats.AverageMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                metrics["ortMinMs"] = stats.MinMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                metrics["ortMaxMs"] = stats.MaxMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                metrics["ortSamples"] = stats.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return new InspectionModelExecutionResult(
                handle.Config.Id,
                handle.Config.Name,
                handle.Task.TaskKind,
                result.Payload,
                result.DeviceLabel ?? handle.Task.ActiveDeviceLabel,
                metrics);
        }
        finally
        {
            handle.SyncRoot.Release();
        }
    }

    public void Warmup(string modelId, int width = 640, int height = 640, int iterations = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        var handle = _models.GetOrAdd(modelId.Trim(), id => new Lazy<ModelRuntimeHandle>(() => CreateHandle(id))).Value;
        handle.SyncRoot.Wait();
        try
        {
            var warmupWidth = handle.Config.InputWidth > 0 ? handle.Config.InputWidth : width;
            var warmupHeight = handle.Config.InputHeight > 0 ? handle.Config.InputHeight : height;
            for (var i = 0; i < Math.Max(1, iterations); i++)
            {
                handle.Task.Warmup(warmupWidth, warmupHeight);
            }
        }
        finally
        {
            handle.SyncRoot.Release();
        }
    }

    public IReadOnlyList<InspectionModelProbeResult> ProbeAll()
    {
        var settings = InspectionModelSettingsStorage.Load(_modelConfigPath);
        return settings.Models
            .Where(model => model.Enabled)
            .Select(Probe)
            .ToArray();
    }

    public InspectionModelProbeResult Probe(InspectionModelConfig model)
    {
        try
        {
            var normalized = model.Normalize(1);
            if (string.IsNullOrWhiteSpace(normalized.ModelPath) || !File.Exists(normalized.ModelPath))
            {
                return new InspectionModelProbeResult(
                    normalized.Id,
                    normalized.Name,
                    false,
                    $"ONNX 文件不存在: {normalized.ModelPath}");
            }

            if (normalized.DeviceKind == InferenceDeviceKind.GpuRt &&
                !HasTensorRtCache(normalized, out var cacheDirectory))
            {
                return new InspectionModelProbeResult(
                    normalized.Id,
                    normalized.Name,
                    false,
                    $"TensorRT cache 不存在，请先构建: {cacheDirectory}");
            }

            using var task = CreateTask(normalized);
            var width = normalized.InputWidth > 0 ? normalized.InputWidth : 640;
            var height = normalized.InputHeight > 0 ? normalized.InputHeight : 640;
            task.Warmup(width, height);
            return new InspectionModelProbeResult(
                normalized.Id,
                normalized.Name,
                true,
                $"环境检查通过: {task.ActiveDeviceLabel ?? normalized.DeviceKind.ToString()}");
        }
        catch (Exception ex)
        {
            return new InspectionModelProbeResult(
                model.Id,
                model.Name,
                false,
                ex.Message);
        }
    }

    private static bool HasTensorRtCache(InspectionModelConfig model, out string cacheDirectory)
    {
        cacheDirectory = Path.Combine(AppContext.BaseDirectory, "trt-cache", model.Id);
        return Directory.Exists(cacheDirectory) &&
               Directory.EnumerateFiles(cacheDirectory, "*", SearchOption.AllDirectories).Any();
    }

    private ModelRuntimeHandle CreateHandle(string modelId)
    {
        var settings = InspectionModelSettingsStorage.Load(_modelConfigPath);
        var model = settings.Models.FirstOrDefault(item => string.Equals(item.Id, modelId, StringComparison.OrdinalIgnoreCase));
        if (model == null)
        {
            throw new InvalidOperationException($"模型 '{modelId}' 未在模型配置中找到。");
        }

        var normalized = model.Normalize(1);
        return new ModelRuntimeHandle(normalized, CreateTask(normalized));
    }

    private static IVisionTask CreateTask(InspectionModelConfig model)
    {
        var definition = new VisionTaskDefinition
        {
            Id = model.Id,
            DisplayName = model.Name,
            TaskKind = ToVisionTaskKind(model.TaskType),
            RuntimeKind = model.TaskType is ModelTaskType.OcrText or ModelTaskType.OcrPipeline
                ? VisionRuntimeKind.OcrRuntime
                : model.Runtime,
            BundlePath = model.BundleDirectory,
            ConfigPath = model.ClassConfigPath,
            Metadata = BuildMetadata(model)
        };

        var context = new VisionTaskCreationContext(
            model.DeviceKind,
            ResolveThreshold(model.Yolo.MinScore, 0.25f),
            ResolveThreshold(model.Yolo.NmsThreshold, 0.45f));

        if (model.TaskType == ModelTaskType.OcrPipeline)
        {
            return new RapidOcrPipelineTask(definition);
        }

        return definition.RuntimeKind == VisionRuntimeKind.OcrRuntime
            ? OcrVisionTaskFactory.Instance.Create(definition, context)
            : OnnxVisionTaskFactory.Instance.Create(definition, context);
    }

    private static IReadOnlyDictionary<string, string> BuildMetadata(InspectionModelConfig model)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["modelPath"] = model.ModelPath,
            ["bundleDirectory"] = string.IsNullOrWhiteSpace(model.BundleDirectory)
                ? Path.GetDirectoryName(model.ModelPath) ?? AppContext.BaseDirectory
                : model.BundleDirectory,
            ["classConfigPath"] = model.ClassConfigPath
        };

        foreach (var parameter in model.Parameters)
        {
            if (!string.IsNullOrWhiteSpace(parameter.Name))
            {
                metadata[NormalizeParameterName(parameter.Name)] = parameter.Value;
            }
        }

        metadata.TryAdd("yolo.outputLayout", model.Yolo.OutputLayout.ToString());
        metadata.TryAdd("yolo.scoreMode", model.Yolo.ScoreMode.ToString());
        if (model.Yolo.ClassCount > 0)
        {
            metadata.TryAdd("yolo.classCount", model.Yolo.ClassCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (model.Yolo.MinScore > 0f)
        {
            metadata.TryAdd("yolo.minScore", model.Yolo.MinScore.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (model.Yolo.NmsThreshold > 0f)
        {
            metadata.TryAdd("yolo.nmsThreshold", model.Yolo.NmsThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(model.Yolo.DetectionOutputName))
        {
            metadata.TryAdd("yolo.detectionOutputName", model.Yolo.DetectionOutputName);
        }

        if (!string.IsNullOrWhiteSpace(model.Yolo.PrototypeOutputName))
        {
            metadata.TryAdd("yolo.prototypeOutputName", model.Yolo.PrototypeOutputName);
        }

        metadata.TryAdd("yolo.maskThreshold", model.Yolo.MaskThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (model.TaskType == ModelTaskType.ObbDetection)
        {
            metadata.TryAdd("locator.minScore", model.Yolo.LocatorMinScore.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        metadata.TryAdd("inputWidth", model.InputWidth.ToString(System.Globalization.CultureInfo.InvariantCulture));
        metadata.TryAdd("inputHeight", model.InputHeight.ToString(System.Globalization.CultureInfo.InvariantCulture));
        metadata.TryAdd("yolo.tensorRtCacheKey", model.Id);

        if (model.TaskType == ModelTaskType.OcrText)
        {
            var dictPath = ResolveOcrDictPath(model);
            if (!string.IsNullOrWhiteSpace(dictPath))
            {
                metadata["dictPath"] = dictPath;
            }

            if (model.InputWidth <= 0 || model.InputHeight <= 0)
            {
                throw new InvalidOperationException($"OCR model '{model.Id}' must declare inputWidth/inputHeight in model.json.");
            }

            metadata["fixedWidth"] = model.InputWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["fixedHeight"] = model.InputHeight.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (model.TaskType == ModelTaskType.OcrPipeline)
        {
            var ocr = model.Ocr.Normalize();
            metadata["ocr.detPath"] = ocr.DetPath;
            metadata["ocr.clsPath"] = ocr.ClsPath;
            metadata["ocr.recPath"] = ocr.RecPath;
            metadata["ocr.dictPath"] = ocr.DictPath;
            metadata["ocr.doAngle"] = ocr.DoAngle ? "true" : "false";
            metadata["ocr.returnWordBox"] = ocr.ReturnWordBox ? "true" : "false";
        }
        else if (model.TaskType == ModelTaskType.UnetSegmentation)
        {
            var unet = model.Unet.Normalize();
            metadata["unet.probabilityThreshold"] = unet.ProbabilityThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["unet.minComponentArea"] = unet.MinComponentArea.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["unet.minComponentPerimeter"] = unet.MinComponentPerimeter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["unet.minAreaPerimeterRatio"] = unet.MinAreaPerimeterRatio.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["unet.maxAreaPerimeterRatio"] = unet.MaxAreaPerimeterRatio.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["unet.tensorRtCacheKey"] = model.Id;
        }
        else if (model.TaskType == ModelTaskType.PresenceClassification)
        {
            var classification = model.Classification.Normalize();
            metadata["classification.presentClass"] = classification.PresentClass;
            metadata["classification.absentClass"] = classification.AbsentClass;
            metadata["classification.probabilityThreshold"] = classification.ProbabilityThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["classification.tensorRtCacheKey"] = model.Id;
        }

        return metadata;
    }

    private static float ResolveThreshold(float value, float fallback)
    {
        return value > 0f && value < 1f ? value : fallback;
    }

    private static string ResolveOcrDictPath(InspectionModelConfig model)
    {
        var manifestPath = model.ClassConfigPath;
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            var bundleDirectory = string.IsNullOrWhiteSpace(model.BundleDirectory)
                ? Path.GetDirectoryName(manifestPath) ?? AppContext.BaseDirectory
                : model.BundleDirectory;
            var dictFile = ReadManifestString(root, "dictFile");
            if (string.IsNullOrWhiteSpace(dictFile))
            {
                dictFile = ReadManifestString(root, "dictPath");
            }

            if (string.IsNullOrWhiteSpace(dictFile))
            {
                return string.Empty;
            }

            return Path.IsPathRooted(dictFile)
                ? dictFile
                : Path.GetFullPath(Path.Combine(bundleDirectory, dictFile));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadManifestString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var element) &&
               element.ValueKind == JsonValueKind.String
            ? element.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static string NormalizeParameterName(string name)
    {
        var trimmed = name.Trim();
        return trimmed.Contains('.', StringComparison.Ordinal)
            ? trimmed
            : $"yolo.{trimmed}";
    }

    private static VisionTaskKind ToVisionTaskKind(ModelTaskType taskType)
    {
        return taskType switch
        {
            ModelTaskType.UnetSegmentation => VisionTaskKind.UnetSegmentation,
            ModelTaskType.PresenceClassification => VisionTaskKind.PresenceClassification,
            ModelTaskType.ObbDetection => VisionTaskKind.ObbDetection,
            ModelTaskType.Segmentation => VisionTaskKind.Segmentation,
            ModelTaskType.SequenceBands => VisionTaskKind.SequenceBands,
            ModelTaskType.OcrText => VisionTaskKind.OcrText,
            ModelTaskType.OcrPipeline => VisionTaskKind.OcrPipeline,
            _ => VisionTaskKind.Detection
        };
    }

    public void Dispose()
    {
        foreach (var lazy in _models.Values)
        {
            if (lazy.IsValueCreated)
            {
                lazy.Value.Dispose();
            }
        }

        _models.Clear();
    }

    private sealed class ModelRuntimeHandle : IDisposable
    {
        public ModelRuntimeHandle(InspectionModelConfig config, IVisionTask task)
        {
            Config = config;
            Task = task;
        }

        public InspectionModelConfig Config { get; }

        public IVisionTask Task { get; }

        public SemaphoreSlim SyncRoot { get; } = new(1, 1);

        public void Dispose()
        {
            SyncRoot.Dispose();
            Task.Dispose();
        }
    }

    private sealed class LatencyWindow
    {
        private readonly int _capacity;
        private readonly Queue<double> _values = new();
        private readonly object _syncRoot = new();

        public LatencyWindow(int capacity)
        {
            _capacity = Math.Max(1, capacity);
        }

        public LatencyStats Add(double value)
        {
            lock (_syncRoot)
            {
                _values.Enqueue(value);
                while (_values.Count > _capacity)
                {
                    _values.Dequeue();
                }

                return new LatencyStats(
                    _values.Count,
                    _values.Min(),
                    _values.Max(),
                    _values.Average());
            }
        }
    }
}

public sealed record LatencyStats(
    int Count,
    double MinMs,
    double MaxMs,
    double AverageMs);

public sealed record InspectionModelExecutionResult(
    string ModelId,
    string ModelName,
    VisionTaskKind TaskKind,
    VisionTaskPayload Payload,
    string? DeviceLabel,
    IReadOnlyDictionary<string, string> Metrics);

public sealed record InspectionModelProbeResult(
    string ModelId,
    string ModelName,
    bool Success,
    string Message);
