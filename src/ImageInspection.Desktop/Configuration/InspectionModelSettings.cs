using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VideoInferenceDemo.ImageInspection;

public sealed class InspectionModelSettings
{
    public List<InspectionModelConfig> Models { get; set; } = [];

    public static InspectionModelSettings CreateDefault()
    {
        return CreateDefault(AppContext.BaseDirectory);
    }

    public static InspectionModelSettings CreateDefault(string baseDirectory)
    {
        var discovered = CreateFromDiscoveredModels(DiscoverDlModels(baseDirectory));
        if (discovered.Models.Count > 0)
        {
            return discovered;
        }

        return new InspectionModelSettings
        {
            Models =
            [
                new InspectionModelConfig
                {
                    Id = "default-yolo",
                    Name = "Default YOLO",
                    TaskType = ModelTaskType.Detection,
                    Runtime = VisionRuntimeKind.OnnxRuntime,
                    Enabled = true,
                    Yolo = new InspectionYoloMetadataConfig()
                }
            ]
        };
    }

    public static IReadOnlyList<ModelCatalogEntry> DiscoverDlModels(string baseDirectory)
    {
        var resolvedBaseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : baseDirectory;
        return ModelCatalog.Discover(Path.Combine(resolvedBaseDirectory, "DL"));
    }

    public static InspectionModelSettings CreateFromDiscoveredModels(IReadOnlyList<ModelCatalogEntry> entries)
    {
        return new InspectionModelSettings
        {
            Models = entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.ModelPath))
                .Select(FromCatalogEntry)
                .ToList()
        };
    }

    public static InspectionModelSettings MergeDiscoveredModels(
        InspectionModelSettings? existing,
        IReadOnlyList<ModelCatalogEntry> discoveredEntries)
    {
        existing ??= new InspectionModelSettings();
        existing.Models ??= [];

        var discovered = CreateFromDiscoveredModels(discoveredEntries).Models;
        if (discovered.Count == 0)
        {
            return existing;
        }

        var existingById = existing.Models
            .Where(model => model != null && !string.IsNullOrWhiteSpace(model.Id))
            .GroupBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var merged = new List<InspectionModelConfig>();
        foreach (var discoveredModel in discovered)
        {
            if (existingById.TryGetValue(discoveredModel.Id, out var existingModel))
            {
                merged.Add(MergeModel(existingModel, discoveredModel));
                existingById.Remove(discoveredModel.Id);
            }
            else
            {
                merged.Add(discoveredModel);
            }
        }

        merged.AddRange(existing.Models
            .Where(model => model != null && existingById.ContainsKey(model.Id)));

        return new InspectionModelSettings
        {
            Models = merged
        };
    }

    private static InspectionModelConfig FromCatalogEntry(ModelCatalogEntry entry)
    {
        var metadata = LoadMetadataValues(entry);
        var yolo = new InspectionYoloMetadataConfig();
        if (entry.YoloMetadata != null)
        {
            yolo.OutputLayout = entry.YoloMetadata.OutputLayout;
            yolo.ScoreMode = entry.YoloMetadata.ScoreMode;
            yolo.ClassCount = entry.YoloMetadata.ClassCount ?? 0;
            yolo.MinScore = entry.YoloMetadata.MinScore;
            yolo.NmsThreshold = entry.YoloMetadata.NmsThreshold;
            yolo.TensorRtCacheKey = entry.YoloMetadata.TensorRtCacheKey ?? entry.Id;
        }
        else if (entry.YoloObbMetadata != null)
        {
            yolo.OutputLayout = entry.YoloObbMetadata.OutputLayout;
            yolo.ScoreMode = entry.YoloObbMetadata.ScoreMode;
            yolo.ClassCount = entry.YoloObbMetadata.ClassCount ?? 0;
            yolo.MinScore = entry.YoloObbMetadata.MinScore;
            yolo.NmsThreshold = entry.YoloObbMetadata.NmsThreshold;
            yolo.LocatorMinScore = entry.YoloObbMetadata.LocatorMinScore;
            yolo.TensorRtCacheKey = entry.YoloObbMetadata.TensorRtCacheKey ?? entry.Id;
        }
        else if (entry.YoloSegmentationMetadata != null)
        {
            yolo.OutputLayout = entry.YoloSegmentationMetadata.OutputLayout;
            yolo.ScoreMode = entry.YoloSegmentationMetadata.ScoreMode;
            yolo.ClassCount = entry.YoloSegmentationMetadata.ClassCount ?? 0;
            yolo.MinScore = entry.YoloSegmentationMetadata.MinScore;
            yolo.NmsThreshold = entry.YoloSegmentationMetadata.NmsThreshold;
            yolo.DetectionOutputName = entry.YoloSegmentationMetadata.DetectionOutputName ?? string.Empty;
            yolo.PrototypeOutputName = entry.YoloSegmentationMetadata.PrototypeOutputName ?? string.Empty;
            yolo.MaskThreshold = entry.YoloSegmentationMetadata.MaskThreshold;
            yolo.TensorRtCacheKey = entry.YoloSegmentationMetadata.TensorRtCacheKey ?? entry.Id;
        }

        var unet = entry.UnetSegmentationMetadata == null
            ? new InspectionUnetSegmentationConfig()
            : InspectionUnetSegmentationConfig.FromMetadata(entry.UnetSegmentationMetadata);
        var classification = entry.PresenceClassificationMetadata == null
            ? new InspectionPresenceClassificationConfig()
            : InspectionPresenceClassificationConfig.FromMetadata(entry.PresenceClassificationMetadata);

        return new InspectionModelConfig
        {
            Id = entry.Id,
            Name = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.Id : entry.DisplayName,
            Description = entry.Description,
            ModelPath = entry.ModelPath,
            BundleDirectory = entry.BundleDirectory,
            ClassConfigPath = entry.ClassConfigPath,
            TaskType = entry.TaskType,
            Runtime = entry.TaskType == ModelTaskType.OcrText
                ? VisionRuntimeKind.OcrRuntime
                : entry.TaskType == ModelTaskType.OcrPipeline
                    ? VisionRuntimeKind.OcrRuntime
                : VisionRuntimeKind.OnnxRuntime,
            DeviceKind = InferenceDeviceKind.GpuCuda,
            Enabled = true,
            SharedRuntime = true,
            InputWidth = metadata.InputWidth,
            InputHeight = metadata.InputHeight,
            Classes = metadata.Classes,
            ClassColors = metadata.ClassColors,
            Yolo = yolo,
            Unet = unet,
            Classification = classification,
            Ocr = FromCatalogEntry(entry.OcrPipelineMetadata),
            Parameters = BuildParameters(entry.TaskType, yolo, unet, classification)
        };
    }

    private static InspectionOcrPipelineConfig FromCatalogEntry(OcrPipelineMetadata? metadata)
    {
        if (metadata == null)
        {
            return new InspectionOcrPipelineConfig();
        }

        return new InspectionOcrPipelineConfig
        {
            DetPath = metadata.DetPath,
            ClsPath = metadata.ClsPath,
            RecPath = metadata.RecPath,
            DictPath = metadata.DictPath,
            DoAngle = metadata.DoAngle,
            ReturnWordBox = metadata.ReturnWordBox
        };
    }

    private static List<InspectionModelParameter> BuildParameters(
        ModelTaskType taskType,
        InspectionYoloMetadataConfig yolo,
        InspectionUnetSegmentationConfig unet,
        InspectionPresenceClassificationConfig classification)
    {
        if (taskType == ModelTaskType.UnetSegmentation)
        {
            var normalized = unet.Normalize();
            return
            [
                new() { Name = "unet.probabilityThreshold", Value = normalized.ProbabilityThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                new() { Name = "unet.minComponentArea", Value = normalized.MinComponentArea.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                new() { Name = "unet.minComponentPerimeter", Value = normalized.MinComponentPerimeter.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                new() { Name = "unet.minAreaPerimeterRatio", Value = normalized.MinAreaPerimeterRatio.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                new() { Name = "unet.maxAreaPerimeterRatio", Value = normalized.MaxAreaPerimeterRatio.ToString(System.Globalization.CultureInfo.InvariantCulture) }
            ];
        }

        if (taskType == ModelTaskType.PresenceClassification)
        {
            var normalized = classification.Normalize();
            return
            [
                new() { Name = "classification.presentClass", Value = normalized.PresentClass },
                new() { Name = "classification.absentClass", Value = normalized.AbsentClass },
                new() { Name = "classification.probabilityThreshold", Value = normalized.ProbabilityThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture) }
            ];
        }

        var parameters = new List<InspectionModelParameter>
        {
            new() { Name = "outputLayout", Value = yolo.OutputLayout.ToString() },
            new() { Name = "scoreMode", Value = yolo.ScoreMode.ToString() }
        };

        if (!string.IsNullOrWhiteSpace(yolo.DetectionOutputName))
        {
            parameters.Add(new InspectionModelParameter { Name = "detectionOutputName", Value = yolo.DetectionOutputName });
        }

        if (!string.IsNullOrWhiteSpace(yolo.PrototypeOutputName))
        {
            parameters.Add(new InspectionModelParameter { Name = "prototypeOutputName", Value = yolo.PrototypeOutputName });
        }

        if (yolo.MinScore > 0f)
        {
            parameters.Add(new InspectionModelParameter { Name = "yolo.minScore", Value = yolo.MinScore.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }

        if (yolo.NmsThreshold > 0f)
        {
            parameters.Add(new InspectionModelParameter { Name = "yolo.nmsThreshold", Value = yolo.NmsThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }

        parameters.Add(new InspectionModelParameter { Name = "maskThreshold", Value = yolo.MaskThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        if (yolo.LocatorMinScore > 0f)
        {
            parameters.Add(new InspectionModelParameter { Name = "locator.minScore", Value = yolo.LocatorMinScore.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }
        return parameters;
    }

    private static InspectionModelConfig MergeModel(InspectionModelConfig existing, InspectionModelConfig discovered)
    {
        return new InspectionModelConfig
        {
            Id = discovered.Id,
            Name = string.IsNullOrWhiteSpace(existing.Name) ? discovered.Name : existing.Name.Trim(),
            Description = discovered.Description,
            ModelPath = discovered.ModelPath,
            BundleDirectory = discovered.BundleDirectory,
            ClassConfigPath = discovered.ClassConfigPath,
            TaskType = discovered.TaskType,
            Runtime = discovered.Runtime,
            DeviceKind = InferenceDeviceKind.GpuCuda,
            Enabled = existing.Enabled,
            SharedRuntime = existing.SharedRuntime,
            InputWidth = discovered.InputWidth,
            InputHeight = discovered.InputHeight,
            Classes = discovered.Classes,
            ClassColors = discovered.ClassColors,
            Yolo = discovered.Yolo,
            Unet = discovered.Unet,
            Classification = discovered.Classification,
            Ocr = discovered.Ocr,
            Parameters = discovered.Parameters
        };
    }

    private static ModelMetadataValues LoadMetadataValues(ModelCatalogEntry entry)
    {
        var metadata = LoadMetadataValuesFromFile(entry.ClassConfigPath);
        if (SequenceModelMetadata.TryLoadForModel(entry.ModelPath, out var sequence))
        {
            metadata = metadata with
            {
                Classes = metadata.Classes.Count > 0 ? metadata.Classes : sequence.ClassNames.ToList(),
                InputWidth = metadata.InputWidth > 0 ? metadata.InputWidth : sequence.InputWidth,
                InputHeight = metadata.InputHeight > 0 ? metadata.InputHeight : sequence.InputHeight
            };
        }

        return metadata;
    }

    private static ModelMetadataValues LoadMetadataValuesFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new ModelMetadataValues([], [], 0, 0);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                return new ModelMetadataValues(ReadStringArray(root), [], 0, 0);
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new ModelMetadataValues([], [], 0, 0);
            }

            var classes = ReadStringArray(root, "classes", "classNames");
            var colors = ReadStringArray(root, "boxColors", "colors");
            if (colors.Count == 0)
            {
                colors = ReadStringArray(root, "boxColor", "color");
            }

            var inputWidth = ReadInt(root, "inputWidth");
            var inputHeight = ReadInt(root, "inputHeight");

            if (root.TryGetProperty("sequence", out var sequence) && sequence.ValueKind == JsonValueKind.Object)
            {
                if (classes.Count == 0)
                {
                    classes = ReadStringArray(sequence, "class_names", "classNames", "classes");
                }

            }

            return new ModelMetadataValues(classes, colors, inputWidth, inputHeight);
        }
        catch
        {
            return new ModelMetadataValues([], [], 0, 0);
        }
    }

    private static List<string> ReadStringArray(JsonElement root, params string[] names)
    {
        if (names.Length == 0)
        {
            return ReadStringArrayValue(root);
        }

        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            var values = ReadStringArrayValue(value);
            if (values.Count > 0)
            {
                return values;
            }
        }

        return [];
    }

    private static List<string> ReadStringArrayValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            return string.IsNullOrWhiteSpace(text) ? [] : [text.Trim()];
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            return value
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!.Trim())
                .ToList();
        }

        return [];
    }

    private static int ReadInt(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }
        }

        return 0;
    }

    private sealed record ModelMetadataValues(
        List<string> Classes,
        List<string> ClassColors,
        int InputWidth,
        int InputHeight);
}

public sealed class InspectionModelConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Inspection Model";

    public string Description { get; set; } = string.Empty;

    public string ModelPath { get; set; } = string.Empty;

    public string BundleDirectory { get; set; } = string.Empty;

    public string ClassConfigPath { get; set; } = string.Empty;

    public ModelTaskType TaskType { get; set; } = ModelTaskType.Detection;

    public VisionRuntimeKind Runtime { get; set; } = VisionRuntimeKind.OnnxRuntime;

    public InferenceDeviceKind DeviceKind { get; set; } = InferenceDeviceKind.GpuCuda;

    public bool Enabled { get; set; } = true;

    public bool SharedRuntime { get; set; } = true;

    public int InputWidth { get; set; }

    public int InputHeight { get; set; }

    public List<string> Classes { get; set; } = [];

    public List<string> ClassColors { get; set; } = [];

    public InspectionYoloMetadataConfig Yolo { get; set; } = new();

    public InspectionUnetSegmentationConfig Unet { get; set; } = new();

    public InspectionPresenceClassificationConfig Classification { get; set; } = new();

    public InspectionOcrPipelineConfig Ocr { get; set; } = new();

    public List<InspectionModelParameter> Parameters { get; set; } = [];

    public InspectionModelConfig Normalize(int ordinal)
    {
        var id = NormalizeIdentifier(string.IsNullOrWhiteSpace(Id) ? Name : Id);
        if (string.IsNullOrWhiteSpace(id))
        {
            id = $"model-{Math.Max(1, ordinal)}";
        }

        var modelPath = ModelPath?.Trim() ?? string.Empty;
        var bundleDirectory = BundleDirectory?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(bundleDirectory) && !string.IsNullOrWhiteSpace(modelPath))
        {
            try
            {
                bundleDirectory = Path.GetDirectoryName(Path.GetFullPath(modelPath)) ?? string.Empty;
            }
            catch
            {
                bundleDirectory = string.Empty;
            }
        }

        return new InspectionModelConfig
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(Name) ? $"Model {Math.Max(1, ordinal)}" : Name.Trim(),
            Description = Description?.Trim() ?? string.Empty,
            ModelPath = modelPath,
            BundleDirectory = bundleDirectory,
            ClassConfigPath = ClassConfigPath?.Trim() ?? string.Empty,
            TaskType = TaskType,
            Runtime = Runtime,
            DeviceKind = InferenceDeviceKind.GpuCuda,
            Enabled = Enabled,
            SharedRuntime = SharedRuntime,
            InputWidth = Math.Max(0, InputWidth),
            InputHeight = Math.Max(0, InputHeight),
            Classes = Classes?
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [],
            ClassColors = ClassColors?
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToList() ?? [],
            Yolo = (Yolo ?? new InspectionYoloMetadataConfig()).Normalize(id),
            Unet = (Unet ?? new InspectionUnetSegmentationConfig()).Normalize(),
            Classification = (Classification ?? new InspectionPresenceClassificationConfig()).Normalize(),
            Ocr = (Ocr ?? new InspectionOcrPipelineConfig()).Normalize(),
            Parameters = Parameters?
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => item.Normalize())
                .ToList() ?? []
        };
    }

    public ModelCatalogEntry ToCatalogEntry()
    {
        return new ModelCatalogEntry
        {
            Id = Id,
            DisplayName = Name,
            Description = Description,
            BundleDirectory = BundleDirectory,
            ModelPath = ModelPath,
            ClassConfigPath = ClassConfigPath,
            TaskType = TaskType,
            IsLegacyRoot = false,
            IsSharedRuntime = SharedRuntime,
            YoloMetadata = TaskType == ModelTaskType.Detection
                ? Yolo.ToDetectionMetadata(Id, Classes.Count)
                : null,
            YoloObbMetadata = TaskType == ModelTaskType.ObbDetection
                ? Yolo.ToObbMetadata(Id, Classes.Count)
                : null,
            YoloSegmentationMetadata = TaskType == ModelTaskType.Segmentation
                ? Yolo.ToSegmentationMetadata(Id, Classes.Count)
                : null,
            UnetSegmentationMetadata = TaskType == ModelTaskType.UnetSegmentation
                ? Unet.ToMetadata(Id)
                : null,
            PresenceClassificationMetadata = TaskType == ModelTaskType.PresenceClassification
                ? Classification.ToMetadata(Id)
                : null,
            OcrPipelineMetadata = TaskType == ModelTaskType.OcrPipeline
                ? Ocr.ToMetadata()
                : null
        };
    }

    private static string NormalizeIdentifier(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        return new string(chars).Trim('-');
    }
}

public sealed class InspectionModelParameter
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public InspectionModelParameter Normalize()
    {
        return new InspectionModelParameter
        {
            Name = Name?.Trim() ?? string.Empty,
            Value = Value?.Trim() ?? string.Empty
        };
    }
}

public sealed class InspectionYoloMetadataConfig
{
    public YoloOutputLayout OutputLayout { get; set; } = YoloOutputLayout.Auto;

    public YoloScoreMode ScoreMode { get; set; } = YoloScoreMode.Auto;

    public int ClassCount { get; set; }

    public string DetectionOutputName { get; set; } = string.Empty;

    public string PrototypeOutputName { get; set; } = string.Empty;

    public float MaskThreshold { get; set; } = 0.5f;

    public float MinScore { get; set; }

    public float NmsThreshold { get; set; }

    public string TensorRtCacheKey { get; set; } = string.Empty;

    public float LocatorMinScore { get; set; }

    public InspectionYoloMetadataConfig Normalize(string modelId)
    {
        return new InspectionYoloMetadataConfig
        {
            OutputLayout = OutputLayout,
            ScoreMode = ScoreMode,
            ClassCount = Math.Max(0, ClassCount),
            DetectionOutputName = DetectionOutputName?.Trim() ?? string.Empty,
            PrototypeOutputName = PrototypeOutputName?.Trim() ?? string.Empty,
            MaskThreshold = MaskThreshold > 0f && MaskThreshold < 1f ? MaskThreshold : 0.5f,
            MinScore = MinScore > 0f && MinScore < 1f ? MinScore : 0f,
            NmsThreshold = NmsThreshold > 0f && NmsThreshold < 1f ? NmsThreshold : 0f,
            LocatorMinScore = LocatorMinScore > 0f && LocatorMinScore < 1f ? LocatorMinScore : 0f,
            TensorRtCacheKey = string.IsNullOrWhiteSpace(TensorRtCacheKey) ? modelId : TensorRtCacheKey.Trim()
        };
    }

    public YoloDetectionMetadata ToDetectionMetadata(string modelId, int classNameCount)
    {
        return new YoloDetectionMetadata
        {
            OutputLayout = OutputLayout,
            ScoreMode = ScoreMode,
            ClassCount = ClassCount > 0 ? ClassCount : classNameCount > 0 ? classNameCount : null,
            MinScore = MinScore,
            NmsThreshold = NmsThreshold,
            TensorRtCacheKey = string.IsNullOrWhiteSpace(TensorRtCacheKey) ? modelId : TensorRtCacheKey
        };
    }

    public YoloSegmentationMetadata ToSegmentationMetadata(string modelId, int classNameCount)
    {
        return new YoloSegmentationMetadata
        {
            OutputLayout = OutputLayout,
            ScoreMode = ScoreMode,
            ClassCount = ClassCount > 0 ? ClassCount : classNameCount > 0 ? classNameCount : null,
            DetectionOutputName = DetectionOutputName,
            PrototypeOutputName = PrototypeOutputName,
            MinScore = MinScore,
            NmsThreshold = NmsThreshold,
            MaskThreshold = MaskThreshold > 0f && MaskThreshold < 1f ? MaskThreshold : 0.5f,
            TensorRtCacheKey = string.IsNullOrWhiteSpace(TensorRtCacheKey) ? modelId : TensorRtCacheKey
        };
    }

    public YoloObbDetectionMetadata ToObbMetadata(string modelId, int classNameCount)
    {
        return new YoloObbDetectionMetadata
        {
            OutputLayout = OutputLayout,
            ScoreMode = ScoreMode,
            ClassCount = ClassCount > 0 ? ClassCount : classNameCount > 0 ? classNameCount : null,
            MinScore = MinScore,
            NmsThreshold = NmsThreshold,
            LocatorMinScore = LocatorMinScore,
            TensorRtCacheKey = string.IsNullOrWhiteSpace(TensorRtCacheKey) ? modelId : TensorRtCacheKey
        };
    }
}

public sealed class InspectionUnetSegmentationConfig
{
    public float ProbabilityThreshold { get; set; } = 0.6f;

    public int MinComponentArea { get; set; } = 20;

    public float MinComponentPerimeter { get; set; }

    public float MinAreaPerimeterRatio { get; set; }

    public float MaxAreaPerimeterRatio { get; set; }

    public string TensorRtCacheKey { get; set; } = string.Empty;

    public InspectionUnetSegmentationConfig Normalize()
    {
        return new InspectionUnetSegmentationConfig
        {
            ProbabilityThreshold = ProbabilityThreshold > 0f && ProbabilityThreshold < 1f ? ProbabilityThreshold : 0.6f,
            MinComponentArea = Math.Max(0, MinComponentArea),
            MinComponentPerimeter = Math.Max(0f, MinComponentPerimeter),
            MinAreaPerimeterRatio = Math.Max(0f, MinAreaPerimeterRatio),
            MaxAreaPerimeterRatio = Math.Max(0f, MaxAreaPerimeterRatio),
            TensorRtCacheKey = TensorRtCacheKey?.Trim() ?? string.Empty
        };
    }

    public UnetSegmentationMetadata ToMetadata(string modelId)
    {
        var normalized = Normalize();
        return new UnetSegmentationMetadata
        {
            ProbabilityThreshold = normalized.ProbabilityThreshold,
            MinComponentArea = normalized.MinComponentArea,
            MinComponentPerimeter = normalized.MinComponentPerimeter,
            MinAreaPerimeterRatio = normalized.MinAreaPerimeterRatio,
            MaxAreaPerimeterRatio = normalized.MaxAreaPerimeterRatio,
            TensorRtCacheKey = string.IsNullOrWhiteSpace(normalized.TensorRtCacheKey) ? modelId : normalized.TensorRtCacheKey
        };
    }

    public static InspectionUnetSegmentationConfig FromMetadata(UnetSegmentationMetadata metadata)
    {
        return new InspectionUnetSegmentationConfig
        {
            ProbabilityThreshold = metadata.ProbabilityThreshold,
            MinComponentArea = metadata.MinComponentArea,
            MinComponentPerimeter = metadata.MinComponentPerimeter,
            MinAreaPerimeterRatio = metadata.MinAreaPerimeterRatio,
            MaxAreaPerimeterRatio = metadata.MaxAreaPerimeterRatio,
            TensorRtCacheKey = metadata.TensorRtCacheKey ?? string.Empty
        }.Normalize();
    }
}

public sealed class InspectionPresenceClassificationConfig
{
    public string PresentClass { get; set; } = "OK";

    public string AbsentClass { get; set; } = "NG";

    public float ProbabilityThreshold { get; set; } = 0.5f;

    public string TensorRtCacheKey { get; set; } = string.Empty;

    public InspectionPresenceClassificationConfig Normalize()
    {
        return new InspectionPresenceClassificationConfig
        {
            PresentClass = string.IsNullOrWhiteSpace(PresentClass) ? "OK" : PresentClass.Trim(),
            AbsentClass = string.IsNullOrWhiteSpace(AbsentClass) ? "NG" : AbsentClass.Trim(),
            ProbabilityThreshold = ProbabilityThreshold > 0f && ProbabilityThreshold < 1f ? ProbabilityThreshold : 0.5f,
            TensorRtCacheKey = TensorRtCacheKey?.Trim() ?? string.Empty
        };
    }

    public PresenceClassificationMetadata ToMetadata(string modelId)
    {
        var normalized = Normalize();
        return new PresenceClassificationMetadata
        {
            PresentClass = normalized.PresentClass,
            AbsentClass = normalized.AbsentClass,
            ProbabilityThreshold = normalized.ProbabilityThreshold,
            TensorRtCacheKey = string.IsNullOrWhiteSpace(normalized.TensorRtCacheKey) ? modelId : normalized.TensorRtCacheKey
        };
    }

    public static InspectionPresenceClassificationConfig FromMetadata(PresenceClassificationMetadata metadata)
    {
        return new InspectionPresenceClassificationConfig
        {
            PresentClass = metadata.PresentClass,
            AbsentClass = metadata.AbsentClass,
            ProbabilityThreshold = metadata.ProbabilityThreshold,
            TensorRtCacheKey = metadata.TensorRtCacheKey ?? string.Empty
        }.Normalize();
    }
}

public sealed class InspectionOcrPipelineConfig
{
    public string DetPath { get; set; } = string.Empty;

    public string ClsPath { get; set; } = string.Empty;

    public string RecPath { get; set; } = string.Empty;

    public string DictPath { get; set; } = string.Empty;

    public bool DoAngle { get; set; }

    public bool ReturnWordBox { get; set; }

    public InspectionOcrPipelineConfig Normalize()
    {
        return new InspectionOcrPipelineConfig
        {
            DetPath = DetPath?.Trim() ?? string.Empty,
            ClsPath = ClsPath?.Trim() ?? string.Empty,
            RecPath = RecPath?.Trim() ?? string.Empty,
            DictPath = DictPath?.Trim() ?? string.Empty,
            DoAngle = DoAngle,
            ReturnWordBox = ReturnWordBox
        };
    }

    public OcrPipelineMetadata ToMetadata()
    {
        return new OcrPipelineMetadata
        {
            DetPath = DetPath,
            ClsPath = ClsPath,
            RecPath = RecPath,
            DictPath = DictPath,
            DoAngle = DoAngle,
            ReturnWordBox = ReturnWordBox
        };
    }
}
