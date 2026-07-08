using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VideoInferenceDemo.ImageInspection.Tasks;

public sealed partial class InspectionModelConfigViewModel : ObservableObject
{
    private static readonly string[] DefaultClassColors =
    [
        "#29B6F6",
        "#66BB6A",
        "#FFA726",
        "#EF5350",
        "#AB47BC",
        "#26A69A",
        "#EC407A",
        "#7E57C2",
        "#5C6BC0",
        "#26C6DA",
        "#9CCC65",
        "#FF7043"
    ];

    private readonly InspectionModelConfig sourceModel;

    public InspectionModelConfigViewModel(InspectionModelConfig model)
    {
        sourceModel = (model ?? new InspectionModelConfig()).Normalize(1);
        Id = sourceModel.Id;
        Name = sourceModel.Name;
        Description = sourceModel.Description;
        ModelPath = sourceModel.ModelPath;
        BundleDirectory = sourceModel.BundleDirectory;
        ClassConfigPath = sourceModel.ClassConfigPath;
        TaskType = sourceModel.TaskType;
        Runtime = sourceModel.Runtime;
        DeviceKind = InferenceDeviceKind.GpuCuda;
        Enabled = sourceModel.Enabled;
        SharedRuntime = sourceModel.SharedRuntime;
        InputWidth = sourceModel.InputWidth;
        InputHeight = sourceModel.InputHeight;
        ClassesText = string.Join("\r\n", sourceModel.Classes);
        ClassColorsText = BuildClassColorsText(sourceModel.ClassColors, sourceModel.Classes.Count);
        OutputLayout = sourceModel.Yolo.OutputLayout;
        ScoreMode = sourceModel.Yolo.ScoreMode;
        ClassCount = sourceModel.Yolo.ClassCount;
        DetectionOutputName = sourceModel.Yolo.DetectionOutputName;
        PrototypeOutputName = sourceModel.Yolo.PrototypeOutputName;
        MaskThreshold = sourceModel.Yolo.MaskThreshold;
        MinScore = sourceModel.Yolo.MinScore;
        NmsThreshold = sourceModel.Yolo.NmsThreshold;
        LocatorMinScore = sourceModel.Yolo.LocatorMinScore;
        TensorRtCacheKey = sourceModel.Yolo.TensorRtCacheKey;
        var unet = sourceModel.Unet.Normalize();
        UnetProbabilityThresholdText = unet.ProbabilityThreshold.ToString(CultureInfo.InvariantCulture);
        UnetMinComponentAreaText = unet.MinComponentArea.ToString(CultureInfo.InvariantCulture);
        UnetMinComponentPerimeterText = unet.MinComponentPerimeter.ToString(CultureInfo.InvariantCulture);
        UnetMinAreaPerimeterRatioText = unet.MinAreaPerimeterRatio.ToString(CultureInfo.InvariantCulture);
        UnetMaxAreaPerimeterRatioText = unet.MaxAreaPerimeterRatio.ToString(CultureInfo.InvariantCulture);

        foreach (var parameter in sourceModel.Parameters)
        {
            Parameters.Add(new InspectionModelParameterViewModel(parameter));
        }

        MetadataRows = BuildMetadataRows(sourceModel);
        ClassRows = BuildClassRows(sourceModel);
    }

    [ObservableProperty] private string id = string.Empty;
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private string modelPath = string.Empty;
    [ObservableProperty] private string bundleDirectory = string.Empty;
    [ObservableProperty] private string classConfigPath = string.Empty;
    [ObservableProperty] private ModelTaskType taskType = ModelTaskType.Detection;
    [ObservableProperty] private VisionRuntimeKind runtime = VisionRuntimeKind.OnnxRuntime;
    [ObservableProperty] private InferenceDeviceKind deviceKind = InferenceDeviceKind.GpuCuda;
    [ObservableProperty] private bool enabled = true;
    [ObservableProperty] private bool sharedRuntime = true;
    [ObservableProperty] private int inputWidth;
    [ObservableProperty] private int inputHeight;
    [ObservableProperty] private string classesText = string.Empty;
    [ObservableProperty] private string classColorsText = string.Empty;
    [ObservableProperty] private YoloOutputLayout outputLayout = YoloOutputLayout.Auto;
    [ObservableProperty] private YoloScoreMode scoreMode = YoloScoreMode.Auto;
    [ObservableProperty] private int classCount;
    [ObservableProperty] private string detectionOutputName = string.Empty;
    [ObservableProperty] private string prototypeOutputName = string.Empty;
    [ObservableProperty] private float maskThreshold = 0.5f;
    [ObservableProperty] private float minScore;
    [ObservableProperty] private float nmsThreshold;
    [ObservableProperty] private float locatorMinScore;
    [ObservableProperty] private string tensorRtCacheKey = string.Empty;
    [ObservableProperty] private string unetProbabilityThresholdText = "0.6";
    [ObservableProperty] private string unetMinComponentAreaText = "20";
    [ObservableProperty] private string unetMinComponentPerimeterText = "0";
    [ObservableProperty] private string unetMinAreaPerimeterRatioText = "0";
    [ObservableProperty] private string unetMaxAreaPerimeterRatioText = "0";

    public ObservableCollection<InspectionModelParameterViewModel> Parameters { get; } = [];

    public IReadOnlyList<InspectionModelMetadataRow> MetadataRows { get; }

    public IReadOnlyList<InspectionModelClassRow> ClassRows { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;

    public string SecondaryText => TaskType == ModelTaskType.OcrPipeline
        ? $"{FormatTaskType(TaskType)} / RapidOcrNet CPU"
        : $"{FormatTaskType(TaskType)} / 自动设备";

    public string DevicePolicyText => TaskType == ModelTaskType.OcrPipeline
        ? "OCR 管线：RapidOcrNet / CPU"
        : "自动：有 TensorRT cache 时 TensorRT -> CUDA -> CPU；无 cache 时 CUDA -> CPU";

    public string TensorRtCacheDirectory => Path.Combine(AppContext.BaseDirectory, "trt-cache", EffectiveTensorRtCacheKey);

    public string TensorRtCacheStatusText => TaskType == ModelTaskType.OcrPipeline
        ? "OCR 管线不使用 TensorRT cache。"
        : HasTensorRtCache()
        ? $"TensorRT cache 已存在：{TensorRtCacheDirectory}"
        : $"TensorRT cache 未构建：当前将使用 CUDA -> CPU fallback";

    public string InputSizeText => InputWidth > 0 && InputHeight > 0
        ? $"{InputWidth} x {InputHeight}"
        : "未声明";

    public string ClassCountText => ClassRows.Count > 0
        ? ClassRows.Count.ToString(CultureInfo.InvariantCulture)
        : "未声明";

    public bool IsUnetSegmentation => TaskType == ModelTaskType.UnetSegmentation;

    private string EffectiveTensorRtCacheKey => Id.Trim();

    partial void OnIdChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    partial void OnTaskTypeChanged(ModelTaskType value)
    {
        OnPropertyChanged(nameof(SecondaryText));
        OnPropertyChanged(nameof(IsUnetSegmentation));
    }

    partial void OnModelPathChanged(string value) => OnPropertyChanged(nameof(SecondaryText));

    partial void OnClassesTextChanged(string value)
    {
        ClassColorsText = BuildClassColorsText([], SplitLines(value).Count);
    }

    [RelayCommand]
    private void AddParameter()
    {
        Parameters.Add(new InspectionModelParameterViewModel(new InspectionModelParameter
        {
            Name = $"parameter{Parameters.Count + 1}"
        }));
    }

    [RelayCommand]
    private void RemoveParameter(InspectionModelParameterViewModel? parameter)
    {
        if (parameter != null)
        {
            Parameters.Remove(parameter);
        }
    }

    public InspectionModelConfig Build()
    {
        var yolo = sourceModel.Yolo ?? new InspectionYoloMetadataConfig();
        return new InspectionModelConfig
        {
            Id = sourceModel.Id,
            Name = Name,
            Description = sourceModel.Description,
            ModelPath = sourceModel.ModelPath,
            BundleDirectory = sourceModel.BundleDirectory,
            ClassConfigPath = sourceModel.ClassConfigPath,
            TaskType = sourceModel.TaskType,
            Runtime = sourceModel.Runtime,
            DeviceKind = InferenceDeviceKind.GpuCuda,
            Enabled = Enabled,
            SharedRuntime = sourceModel.SharedRuntime,
            InputWidth = sourceModel.InputWidth,
            InputHeight = sourceModel.InputHeight,
            Classes = sourceModel.Classes.ToList(),
            ClassColors = sourceModel.ClassColors.ToList(),
            Yolo = new InspectionYoloMetadataConfig
            {
                OutputLayout = yolo.OutputLayout,
                ScoreMode = yolo.ScoreMode,
                ClassCount = yolo.ClassCount,
                DetectionOutputName = yolo.DetectionOutputName,
                PrototypeOutputName = yolo.PrototypeOutputName,
                MaskThreshold = yolo.MaskThreshold,
                MinScore = yolo.MinScore,
                NmsThreshold = yolo.NmsThreshold,
                LocatorMinScore = yolo.LocatorMinScore,
                TensorRtCacheKey = yolo.TensorRtCacheKey
            },
            Unet = new InspectionUnetSegmentationConfig
            {
                ProbabilityThreshold = ParseFloat(UnetProbabilityThresholdText, 0.6f),
                MinComponentArea = ParseInt(UnetMinComponentAreaText, 20),
                MinComponentPerimeter = ParseFloat(UnetMinComponentPerimeterText, 0f),
                MinAreaPerimeterRatio = ParseFloat(UnetMinAreaPerimeterRatioText, 0f),
                MaxAreaPerimeterRatio = ParseFloat(UnetMaxAreaPerimeterRatioText, 0f),
                TensorRtCacheKey = sourceModel.Unet.TensorRtCacheKey
            }.Normalize(),
            Classification = sourceModel.Classification.Normalize(),
            Ocr = sourceModel.Ocr.Normalize(),
            Parameters = sourceModel.Parameters.Select(parameter => parameter.Normalize()).ToList()
        };
    }

    public void RefreshDiagnostics()
    {
        OnPropertyChanged(nameof(TensorRtCacheStatusText));
    }

    private static List<string> SplitLines(string value)
    {
        return value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    private static List<string> ResolveClassColors(int count)
    {
        return Enumerable
            .Range(0, Math.Max(0, count))
            .Select(index => DefaultClassColors[index % DefaultClassColors.Length])
            .ToList();
    }

    private static string BuildClassColorsText(IReadOnlyList<string> colors, int classCount)
    {
        var resolved = colors
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .ToList();

        if (resolved.Count == 0)
        {
            resolved = ResolveClassColors(classCount);
        }

        return string.Join("\r\n", resolved);
    }

    private bool HasTensorRtCache()
    {
        return Directory.Exists(TensorRtCacheDirectory) &&
               Directory.EnumerateFiles(TensorRtCacheDirectory, "*", SearchOption.AllDirectories).Any();
    }

    private static IReadOnlyList<InspectionModelClassRow> BuildClassRows(InspectionModelConfig model)
    {
        var colors = model.ClassColors.Count > 0
            ? model.ClassColors
            : ResolveClassColors(model.Classes.Count);
        return model.Classes
            .Select((name, index) => new InspectionModelClassRow(
                index + 1,
                name,
                index < colors.Count ? colors[index] : DefaultClassColors[index % DefaultClassColors.Length]))
            .ToList();
    }

    private static IReadOnlyList<InspectionModelMetadataRow> BuildMetadataRows(InspectionModelConfig model)
    {
        var rows = new List<InspectionModelMetadataRow>
        {
            new("任务类型", FormatTaskType(model.TaskType)),
            new("运行时", model.TaskType == ModelTaskType.OcrPipeline ? "RapidOcrNet / CPU" : model.Runtime.ToString()),
            new("输入尺寸", model.InputWidth > 0 && model.InputHeight > 0 ? $"{model.InputWidth} x {model.InputHeight}" : "未声明"),
            new("类别数量", model.Classes.Count > 0 ? model.Classes.Count.ToString(CultureInfo.InvariantCulture) : "未声明"),
            new("TensorRT Cache Key", model.Id)
        };

        if (!string.IsNullOrWhiteSpace(model.Description))
        {
            rows.Insert(1, new InspectionModelMetadataRow("说明", model.Description));
        }

        if (model.TaskType is ModelTaskType.Detection or ModelTaskType.ObbDetection or ModelTaskType.Segmentation)
        {
            rows.Add(new("YOLO OutputLayout", model.Yolo.OutputLayout.ToString()));
            rows.Add(new("YOLO ScoreMode", model.Yolo.ScoreMode.ToString()));
            if (model.Yolo.ClassCount > 0)
            {
                rows.Add(new("YOLO ClassCount", model.Yolo.ClassCount.ToString(CultureInfo.InvariantCulture)));
            }

            rows.Add(new(
                "YOLO MinScore",
                model.Yolo.MinScore > 0f
                    ? model.Yolo.MinScore.ToString("0.###", CultureInfo.InvariantCulture)
                    : "Not declared"));
            rows.Add(new(
                "YOLO NmsThreshold",
                model.Yolo.NmsThreshold > 0f
                    ? model.Yolo.NmsThreshold.ToString("0.###", CultureInfo.InvariantCulture)
                    : "Not declared"));
        }

        if (model.TaskType == ModelTaskType.ObbDetection)
        {
            rows.Add(new("Locator MinScore", model.Yolo.LocatorMinScore > 0f
                ? model.Yolo.LocatorMinScore.ToString("0.###", CultureInfo.InvariantCulture)
                : "未声明"));
        }

        if (model.TaskType == ModelTaskType.Segmentation)
        {
            AddIfNotEmpty(rows, "Detection Output", model.Yolo.DetectionOutputName);
            AddIfNotEmpty(rows, "Prototype Output", model.Yolo.PrototypeOutputName);
            rows.Add(new("Mask Threshold", model.Yolo.MaskThreshold.ToString("0.###", CultureInfo.InvariantCulture)));
        }

        if (model.TaskType == ModelTaskType.UnetSegmentation)
        {
            var unet = model.Unet.Normalize();
            rows.Add(new("Probability Threshold", unet.ProbabilityThreshold.ToString("0.###", CultureInfo.InvariantCulture)));
            rows.Add(new("Min Component Area", unet.MinComponentArea.ToString(CultureInfo.InvariantCulture)));
            rows.Add(new("Min Component Perimeter", unet.MinComponentPerimeter.ToString("0.###", CultureInfo.InvariantCulture)));
            rows.Add(new("Min Area/Perimeter", unet.MinAreaPerimeterRatio.ToString("0.###", CultureInfo.InvariantCulture)));
            rows.Add(new("Max Area/Perimeter", unet.MaxAreaPerimeterRatio.ToString("0.###", CultureInfo.InvariantCulture)));
        }

        if (model.TaskType == ModelTaskType.PresenceClassification)
        {
            var classification = model.Classification.Normalize();
            rows.Add(new("Present Class", classification.PresentClass));
            rows.Add(new("Absent Class", classification.AbsentClass));
            rows.Add(new("Probability Threshold", classification.ProbabilityThreshold.ToString("0.###", CultureInfo.InvariantCulture)));
        }

        if (model.TaskType == ModelTaskType.OcrPipeline)
        {
            AddIfNotEmpty(rows, "OCR Det", ToDisplayPath(model.Ocr.DetPath, model.BundleDirectory));
            AddIfNotEmpty(rows, "OCR Cls", ToDisplayPath(model.Ocr.ClsPath, model.BundleDirectory));
            AddIfNotEmpty(rows, "OCR Rec", ToDisplayPath(model.Ocr.RecPath, model.BundleDirectory));
            AddIfNotEmpty(rows, "OCR Dict", ToDisplayPath(model.Ocr.DictPath, model.BundleDirectory));
            rows.Add(new("OCR DoAngle", model.Ocr.DoAngle ? "true" : "false"));
            rows.Add(new("OCR ReturnWordBox", model.Ocr.ReturnWordBox ? "true" : "false"));
        }

        foreach (var row in ReadManifestRows(model))
        {
            if (rows.All(existing => !string.Equals(existing.Label, row.Label, StringComparison.OrdinalIgnoreCase)))
            {
                rows.Add(row);
            }
        }

        foreach (var parameter in model.Parameters.Where(item => !string.IsNullOrWhiteSpace(item.Name)))
        {
            rows.Add(new($"参数 {parameter.Name.Trim()}", parameter.Value?.Trim() ?? string.Empty));
        }

        return rows;
    }

    private static IEnumerable<InspectionModelMetadataRow> ReadManifestRows(InspectionModelConfig model)
    {
        var path = model.ClassConfigPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            yield break;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch
        {
            yield break;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                yield break;
            }

            if (model.TaskType == ModelTaskType.SequenceBands &&
                root.TryGetProperty("sequence", out var sequence) &&
                sequence.ValueKind == JsonValueKind.Object)
            {
                foreach (var row in ReadStringRows(sequence, ("input_name", "Sequence Input"), ("output_name", "Sequence Output"), ("sequence_direction", "Sequence Direction"), ("seq_len", "Sequence Length")))
                {
                    yield return row;
                }

                if (sequence.TryGetProperty("preprocess", out var preprocess) && preprocess.ValueKind == JsonValueKind.Object)
                {
                    foreach (var row in ReadStringRows(preprocess, ("image_preprocess", "Preprocess"), ("color_mode", "Color Mode"), ("layout", "Layout"), ("dtype", "DType")))
                    {
                        yield return row;
                    }
                }
            }

            if (model.TaskType == ModelTaskType.OcrText)
            {
                foreach (var row in ReadStringRows(root, ("dictFile", "OCR Dict"), ("inputWidth", "OCR Width"), ("inputHeight", "OCR Height")))
                {
                    yield return row;
                }
            }

            if (model.TaskType == ModelTaskType.OcrPipeline)
            {
                foreach (var row in ReadStringRows(root, ("detFile", "OCR Det"), ("clsFile", "OCR Cls"), ("recFile", "OCR Rec"), ("dictFile", "OCR Dict")))
                {
                    yield return row;
                }

                if (root.TryGetProperty("ocr", out var ocr) && ocr.ValueKind == JsonValueKind.Object)
                {
                    foreach (var row in ReadStringRows(ocr, ("doAngle", "OCR DoAngle"), ("returnWordBox", "OCR ReturnWordBox")))
                    {
                        yield return row;
                    }
                }
            }
        }
    }

    private static string ToDisplayPath(string path, string bundleDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return string.IsNullOrWhiteSpace(bundleDirectory)
                ? path
                : Path.GetRelativePath(bundleDirectory, path);
        }
        catch
        {
            return path;
        }
    }

    private static IEnumerable<InspectionModelMetadataRow> ReadStringRows(JsonElement root, params (string Key, string Label)[] specs)
    {
        foreach (var (key, label) in specs)
        {
            if (!root.TryGetProperty(key, out var value))
            {
                continue;
            }

            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return new InspectionModelMetadataRow(label, text.Trim());
            }
        }
    }

    private static void AddIfNotEmpty(ICollection<InspectionModelMetadataRow> rows, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            rows.Add(new InspectionModelMetadataRow(label, value.Trim()));
        }
    }

    private static string FormatTaskType(ModelTaskType taskType)
    {
        return taskType switch
        {
            ModelTaskType.UnetSegmentation => "U-Net Segmentation",
            ModelTaskType.PresenceClassification => "产品有无分类",
            ModelTaskType.Segmentation => "Segmentation",
            ModelTaskType.ObbDetection => "OBB Detection",
            ModelTaskType.SequenceBands => "Sequence Bands",
            ModelTaskType.OcrText => "OCR Text",
            ModelTaskType.OcrPipeline => "OCR Pipeline",
            _ => "Detection"
        };
    }

    private static float ParseFloat(string value, float fallback)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
               float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed)
            ? parsed
            : fallback;
    }

    private static int ParseInt(string value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
               int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed)
            ? parsed
            : fallback;
    }
}

public sealed record InspectionModelMetadataRow(string Label, string Value);

public sealed record InspectionModelClassRow(int Index, string Name, string Color);

public sealed partial class InspectionModelParameterViewModel : ObservableObject
{
    public InspectionModelParameterViewModel(InspectionModelParameter parameter)
    {
        Name = parameter.Name;
        Value = parameter.Value;
    }

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string value = string.Empty;

    public InspectionModelParameter Build()
    {
        return new InspectionModelParameter
        {
            Name = Name,
            Value = Value
        };
    }
}
