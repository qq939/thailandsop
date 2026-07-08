using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VideoInferenceDemo;

public enum ModelTaskType
{
    Detection,
    ObbDetection,
    Segmentation,
    UnetSegmentation,
    PresenceClassification,
    SequenceBands,
    OcrText,
    OcrPipeline
}

public sealed class ModelCatalogEntry
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string BundleDirectory { get; init; } = string.Empty;
    public string ModelPath { get; init; } = string.Empty;
    public string ClassConfigPath { get; init; } = string.Empty;
    public string DictPath { get; init; } = string.Empty;
    public ModelTaskType TaskType { get; init; }
    public bool IsLegacyRoot { get; init; }
    public bool IsSharedRuntime { get; init; }
    public int InputWidth { get; init; }
    public int InputHeight { get; init; }
    public YoloDetectionMetadata? YoloMetadata { get; init; }
    public YoloObbDetectionMetadata? YoloObbMetadata { get; init; }
    public YoloSegmentationMetadata? YoloSegmentationMetadata { get; init; }
    public UnetSegmentationMetadata? UnetSegmentationMetadata { get; init; }
    public PresenceClassificationMetadata? PresenceClassificationMetadata { get; init; }
    public OcrPipelineMetadata? OcrPipelineMetadata { get; init; }

    public string ModelFileName => Path.GetFileName(ModelPath);

    public string TaskTypeDisplay => TaskType switch
    {
        ModelTaskType.Segmentation => "Segmentation",
        ModelTaskType.UnetSegmentation => "U-Net Segmentation",
        ModelTaskType.PresenceClassification => "产品有无分类",
        ModelTaskType.ObbDetection => "OBB Detection",
        ModelTaskType.SequenceBands => "Sequence",
        ModelTaskType.OcrText => "OCR",
        ModelTaskType.OcrPipeline => "OCR Pipeline",
        _ => "Detection"
    };

    public string RelativeModelPath
    {
        get
        {
            try
            {
                return Path.GetRelativePath(AppContext.BaseDirectory, ModelPath);
            }
            catch
            {
                return ModelPath;
            }
        }
    }

    public override string ToString()
    {
        return DisplayName;
    }
}

public static class ModelCatalog
{
    public static IReadOnlyList<ModelCatalogEntry> Discover(string dlRoot)
    {
        if (string.IsNullOrWhiteSpace(dlRoot) || !Directory.Exists(dlRoot))
        {
            return Array.Empty<ModelCatalogEntry>();
        }

        var bundles = new List<ModelCatalogEntry>();
        foreach (var directory in Directory.GetDirectories(dlRoot).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (TryBuildEntry(directory, isLegacyRoot: false, out var entry))
            {
                bundles.Add(entry);
            }
        }

        if (bundles.Count > 0)
        {
            return bundles;
        }

        return TryBuildEntry(dlRoot, isLegacyRoot: true, out var legacy)
            ? new[] { legacy }
            : Array.Empty<ModelCatalogEntry>();
    }

    private static bool TryBuildEntry(string directory, bool isLegacyRoot, out ModelCatalogEntry entry)
    {
        entry = null!;

        var manifestPath = Path.Combine(directory, "model.json");
        var manifest = LoadManifest(manifestPath);
        var manifestTaskType = ResolveTaskType(manifest, string.Empty);
        var modelPath = manifestTaskType == ModelTaskType.OcrPipeline
            ? ResolveOcrPipelinePrimaryPath(directory, manifest)
            : ResolveModelPath(directory, manifest);
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return false;
        }

        var taskType = ResolveTaskType(manifest, modelPath);
        if (taskType != ModelTaskType.OcrPipeline && !File.Exists(modelPath))
        {
            return false;
        }

        var classConfigPath = ResolveClassConfigPath(directory, manifest);
        var dictPath = ResolveRelativeFile(directory, manifest?.DictFile, manifest?.DictPath);
        var directoryName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var id = NormalizeIdentifier(FirstNonEmpty(
            manifest?.Id,
            isLegacyRoot ? "legacy-root" : directoryName,
            Path.GetFileNameWithoutExtension(modelPath)));
        var displayName = FirstNonEmpty(
            manifest?.DisplayName,
            isLegacyRoot ? "Legacy / DL Root" : directoryName,
            Path.GetFileNameWithoutExtension(modelPath));

        entry = new ModelCatalogEntry
        {
            Id = id,
            DisplayName = displayName,
            Description = FirstNonEmpty(
                manifest?.Description,
                taskType == ModelTaskType.SequenceBands
                    ? "Vertical sequence model with band-style overlay."
                    : taskType == ModelTaskType.OcrText
                        ? "OCR text recognition model."
                        : taskType == ModelTaskType.OcrPipeline
                            ? "OCR detection and recognition pipeline."
                            : taskType == ModelTaskType.UnetSegmentation
                                ? "U-Net binary segmentation model."
                                : taskType == ModelTaskType.PresenceClassification
                                    ? "Product presence classification model."
                                : "Detection model with box overlay."),
            BundleDirectory = directory,
            ModelPath = modelPath,
            ClassConfigPath = classConfigPath,
            DictPath = dictPath,
            TaskType = taskType,
            IsLegacyRoot = isLegacyRoot,
            IsSharedRuntime = manifest?.Shared == true,
            InputWidth = manifest?.InputWidth ?? 0,
            InputHeight = manifest?.InputHeight ?? 0,
            YoloMetadata = taskType == ModelTaskType.Detection
                ? ResolveYoloMetadata(manifest, id)
                : null,
            YoloObbMetadata = taskType == ModelTaskType.ObbDetection
                ? ResolveYoloObbMetadata(manifest, id)
                : null,
            YoloSegmentationMetadata = taskType == ModelTaskType.Segmentation
                ? ResolveYoloSegmentationMetadata(manifest, id)
                : null,
            UnetSegmentationMetadata = taskType == ModelTaskType.UnetSegmentation
                ? ResolveUnetSegmentationMetadata(manifest, id)
                : null,
            PresenceClassificationMetadata = taskType == ModelTaskType.PresenceClassification
                ? ResolvePresenceClassificationMetadata(manifest, id)
                : null,
            OcrPipelineMetadata = taskType == ModelTaskType.OcrPipeline
                ? ResolveOcrPipelineMetadata(manifest, directory)
                : null
        };
        return true;
    }

    private static ModelManifest? LoadManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ModelManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string ResolveModelPath(string directory, ModelManifest? manifest)
    {
        var explicitPath = ResolveRelativeFile(directory, manifest?.ModelFile, manifest?.ModelPath, manifest?.Model);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        var onnxWithMeta = Directory.GetFiles(directory, "*.onnx")
            .Where(SequenceModelMetadata.HasConfigForModel)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(onnxWithMeta))
        {
            return onnxWithMeta;
        }

        var onnx = Directory.GetFiles(directory, "*.onnx")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(onnx))
        {
            return onnx;
        }

        return string.Empty;
    }

    private static string ResolveOcrPipelinePrimaryPath(string directory, ModelManifest? manifest)
    {
        return ResolveRelativePath(
                   directory,
                   manifest?.DetFile,
                   manifest?.DetPath,
                   manifest?.ModelFile,
                   manifest?.ModelPath,
                   manifest?.Model)
               ?? string.Empty;
    }

    private static string ResolveClassConfigPath(string directory, ModelManifest? manifest)
    {
        var explicitPath = ResolveRelativeFile(directory, manifest?.ClassesFile, manifest?.ClassesPath);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        var manifestPath = Path.Combine(directory, "model.json");
        if (File.Exists(manifestPath))
        {
            return manifestPath;
        }

        var defaultPath = Path.Combine(directory, "classes.json");
        return File.Exists(defaultPath) ? defaultPath : string.Empty;
    }

    private static string ResolveRelativeFile(string directory, params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var resolved = Path.IsPathRooted(candidate)
                ? candidate
                : Path.GetFullPath(Path.Combine(directory, candidate));
            if (File.Exists(resolved))
            {
                return resolved;
            }
        }

        return string.Empty;
    }

    private static string? ResolveRelativePath(string directory, params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            return Path.IsPathRooted(candidate)
                ? candidate
                : Path.GetFullPath(Path.Combine(directory, candidate));
        }

        return null;
    }

    private static ModelTaskType ResolveTaskType(ModelManifest? manifest, string modelPath)
    {
        var rawTaskType = manifest?.TaskType;
        if (!string.IsNullOrWhiteSpace(rawTaskType))
        {
            var normalized = rawTaskType.Trim().Replace('-', '_').ToLowerInvariant();
            if (normalized is "sequence" or "sequence_bands" or "bands")
            {
                return ModelTaskType.SequenceBands;
            }

            if (normalized is "detect" or "detection" or "yolo")
            {
                return ModelTaskType.Detection;
            }

            if (normalized is "obb" or "obb_detection" or "yolo_obb" or "yolo_obb_detection" or "rotated_detection")
            {
                return ModelTaskType.ObbDetection;
            }

            if (normalized is "seg" or "segment" or "segmentation" or "yolo_seg" or "yolo_segment" or "yolo_segmentation")
            {
                return ModelTaskType.Segmentation;
            }

            if (normalized is "unet" or "unet_seg" or "unet_segment" or "unet_segmentation" or "semantic_segmentation" or "binary_segmentation" or "scratch_segmentation")
            {
                return ModelTaskType.UnetSegmentation;
            }

            if (normalized is "classification" or "classifier" or "image_classification" or "presence" or "presence_classification" or "product_presence" or "product_presence_classification")
            {
                return ModelTaskType.PresenceClassification;
            }

            if (normalized is "ocr" or "ocr_text" or "ocrtext")
            {
                return ModelTaskType.OcrText;
            }

            if (normalized is "ocr_pipeline" or "ocrpipeline" or "ppocr" or "rapidocr")
            {
                return ModelTaskType.OcrPipeline;
            }
        }

        return Path.GetExtension(modelPath).Equals(".onnx", StringComparison.OrdinalIgnoreCase)
               && (manifest?.Sequence.ValueKind == JsonValueKind.Object || SequenceModelMetadata.HasConfigForModel(modelPath))
            ? ModelTaskType.SequenceBands
            : ModelTaskType.Detection;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string NormalizeIdentifier(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var normalized = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? Guid.NewGuid().ToString("N") : normalized;
    }

    private sealed class ModelManifest
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("taskType")]
        public string? TaskType { get; set; }

        [JsonPropertyName("modelFile")]
        public string? ModelFile { get; set; }

        [JsonPropertyName("modelPath")]
        public string? ModelPath { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("classesFile")]
        public string? ClassesFile { get; set; }

        [JsonPropertyName("classesPath")]
        public string? ClassesPath { get; set; }

        [JsonPropertyName("dictFile")]
        public string? DictFile { get; set; }

        [JsonPropertyName("dictPath")]
        public string? DictPath { get; set; }

        [JsonPropertyName("detFile")]
        public string? DetFile { get; set; }

        [JsonPropertyName("detPath")]
        public string? DetPath { get; set; }

        [JsonPropertyName("clsFile")]
        public string? ClsFile { get; set; }

        [JsonPropertyName("clsPath")]
        public string? ClsPath { get; set; }

        [JsonPropertyName("recFile")]
        public string? RecFile { get; set; }

        [JsonPropertyName("recPath")]
        public string? RecPath { get; set; }

        [JsonPropertyName("shared")]
        public bool Shared { get; set; }

        [JsonPropertyName("inputWidth")]
        public int InputWidth { get; set; }

        [JsonPropertyName("inputHeight")]
        public int InputHeight { get; set; }

        [JsonPropertyName("sequence")]
        public JsonElement Sequence { get; set; }

        [JsonPropertyName("yolo")]
        public JsonElement Yolo { get; set; }

        [JsonPropertyName("unet")]
        public JsonElement Unet { get; set; }

        [JsonPropertyName("classification")]
        public JsonElement Classification { get; set; }

        [JsonPropertyName("locator")]
        public JsonElement Locator { get; set; }

        [JsonPropertyName("ocr")]
        public JsonElement Ocr { get; set; }
    }

    private static YoloDetectionMetadata? ResolveYoloMetadata(ModelManifest? manifest, string cacheKey)
    {
        if (manifest == null || manifest.Yolo.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var config = manifest.Yolo.Deserialize<YoloMetadataFile>(options);
            if (config == null)
            {
                return null;
            }

            return new YoloDetectionMetadata
            {
                OutputLayout = ParseOutputLayout(config.OutputLayout),
                ScoreMode = ParseScoreMode(config.ScoreMode),
                ClassCount = config.ClassCount > 0 ? config.ClassCount : null,
                MinScore = NormalizeThreshold(config.MinScore),
                NmsThreshold = NormalizeThreshold(config.NmsThreshold),
                TensorRtCacheKey = cacheKey
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static YoloOutputLayout ParseOutputLayout(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "channels_first" or "nchw_like" => YoloOutputLayout.ChannelsFirst,
            "boxes_first" or "rows_first" or "box_first" => YoloOutputLayout.BoxesFirst,
            _ => YoloOutputLayout.Auto
        };
    }

    private static YoloScoreMode ParseScoreMode(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "class_only" or "class" => YoloScoreMode.ClassOnly,
            "objectness_and_class" or "objectness" => YoloScoreMode.ObjectnessAndClass,
            _ => YoloScoreMode.Auto
        };
    }

    private static float NormalizeThreshold(float value)
    {
        return value > 0f && value < 1f ? value : 0f;
    }

    private sealed class YoloMetadataFile
    {
        public string? OutputLayout { get; set; }
        public string? ScoreMode { get; set; }
        public int ClassCount { get; set; }
        public string? DetectionOutputName { get; set; }
        public string? PrototypeOutputName { get; set; }
        public float MaskThreshold { get; set; }
        public float MinScore { get; set; }
        public float NmsThreshold { get; set; }
        public float LocatorMinScore { get; set; }
    }

    private sealed class LocatorMetadataFile
    {
        public float MinScore { get; set; }
    }

    private static YoloObbDetectionMetadata? ResolveYoloObbMetadata(ModelManifest? manifest, string cacheKey)
    {
        if (manifest == null || manifest.Yolo.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var config = manifest.Yolo.Deserialize<YoloMetadataFile>(options);
            if (config == null)
            {
                return null;
            }

            var locatorMinScore = config.LocatorMinScore > 0f && config.LocatorMinScore < 1f
                ? config.LocatorMinScore
                : config.MinScore > 0f && config.MinScore < 1f
                    ? config.MinScore
                    : 0f;

            if (manifest.Locator.ValueKind == JsonValueKind.Object)
            {
                var locator = manifest.Locator.Deserialize<LocatorMetadataFile>(options);
                if (locator?.MinScore > 0f && locator.MinScore < 1f)
                {
                    locatorMinScore = locator.MinScore;
                }
            }

            return new YoloObbDetectionMetadata
            {
                OutputLayout = ParseOutputLayout(config.OutputLayout),
                ScoreMode = ParseScoreMode(config.ScoreMode),
                ClassCount = config.ClassCount > 0 ? config.ClassCount : null,
                MinScore = NormalizeThreshold(config.MinScore),
                NmsThreshold = NormalizeThreshold(config.NmsThreshold),
                LocatorMinScore = locatorMinScore,
                TensorRtCacheKey = cacheKey
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static YoloSegmentationMetadata? ResolveYoloSegmentationMetadata(ModelManifest? manifest, string cacheKey)
    {
        if (manifest == null || manifest.Yolo.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var config = manifest.Yolo.Deserialize<YoloMetadataFile>(options);
            if (config == null)
            {
                return null;
            }

            return new YoloSegmentationMetadata
            {
                OutputLayout = ParseOutputLayout(config.OutputLayout),
                ScoreMode = ParseScoreMode(config.ScoreMode),
                ClassCount = config.ClassCount > 0 ? config.ClassCount : null,
                DetectionOutputName = config.DetectionOutputName?.Trim() ?? string.Empty,
                PrototypeOutputName = config.PrototypeOutputName?.Trim() ?? string.Empty,
                MinScore = NormalizeThreshold(config.MinScore),
                NmsThreshold = NormalizeThreshold(config.NmsThreshold),
                MaskThreshold = config.MaskThreshold > 0f && config.MaskThreshold < 1f ? config.MaskThreshold : 0.5f,
                TensorRtCacheKey = cacheKey
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static OcrPipelineMetadata ResolveOcrPipelineMetadata(ModelManifest? manifest, string directory)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        OcrPipelineOptions? ocrOptions = null;
        if (manifest?.Ocr.ValueKind == JsonValueKind.Object)
        {
            try
            {
                ocrOptions = manifest.Ocr.Deserialize<OcrPipelineOptions>(options);
            }
            catch (JsonException)
            {
            }
        }

        return new OcrPipelineMetadata
        {
            DetPath = ResolveRelativePath(directory, manifest?.DetFile, manifest?.DetPath) ?? string.Empty,
            ClsPath = ResolveRelativePath(directory, manifest?.ClsFile, manifest?.ClsPath) ?? string.Empty,
            RecPath = ResolveRelativePath(directory, manifest?.RecFile, manifest?.RecPath) ?? string.Empty,
            DictPath = ResolveRelativePath(directory, manifest?.DictFile, manifest?.DictPath) ?? string.Empty,
            DoAngle = ocrOptions?.DoAngle ?? false,
            ReturnWordBox = ocrOptions?.ReturnWordBox ?? false
        };
    }

    private sealed class OcrPipelineOptions
    {
        public bool DoAngle { get; set; }
        public bool ReturnWordBox { get; set; }
    }

    private sealed class UnetMetadataFile
    {
        public float ProbabilityThreshold { get; set; }
        public int MinComponentArea { get; set; }
        public float MinComponentPerimeter { get; set; }
        public float MinAreaPerimeterRatio { get; set; }
        public float MaxAreaPerimeterRatio { get; set; }
    }

    private sealed class PresenceClassificationMetadataFile
    {
        public string? PresentClass { get; set; }
        public string? AbsentClass { get; set; }
        public float ProbabilityThreshold { get; set; }
    }

    private static UnetSegmentationMetadata ResolveUnetSegmentationMetadata(ModelManifest? manifest, string cacheKey)
    {
        var metadata = new UnetSegmentationMetadata { TensorRtCacheKey = cacheKey };
        if (manifest == null || manifest.Unet.ValueKind != JsonValueKind.Object)
        {
            return metadata;
        }

        try
        {
            var config = manifest.Unet.Deserialize<UnetMetadataFile>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (config == null)
            {
                return metadata;
            }

            return new UnetSegmentationMetadata
            {
                ProbabilityThreshold = config.ProbabilityThreshold > 0f && config.ProbabilityThreshold < 1f
                    ? config.ProbabilityThreshold
                    : metadata.ProbabilityThreshold,
                MinComponentArea = Math.Max(0, config.MinComponentArea),
                MinComponentPerimeter = Math.Max(0f, config.MinComponentPerimeter),
                MinAreaPerimeterRatio = Math.Max(0f, config.MinAreaPerimeterRatio),
                MaxAreaPerimeterRatio = Math.Max(0f, config.MaxAreaPerimeterRatio),
                TensorRtCacheKey = cacheKey
            };
        }
        catch (JsonException)
        {
            return metadata;
        }
    }

    private static PresenceClassificationMetadata ResolvePresenceClassificationMetadata(ModelManifest? manifest, string cacheKey)
    {
        var metadata = new PresenceClassificationMetadata { TensorRtCacheKey = cacheKey };
        if (manifest == null || manifest.Classification.ValueKind != JsonValueKind.Object)
        {
            return metadata;
        }

        try
        {
            var config = manifest.Classification.Deserialize<PresenceClassificationMetadataFile>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (config == null)
            {
                return metadata;
            }

            return new PresenceClassificationMetadata
            {
                PresentClass = string.IsNullOrWhiteSpace(config.PresentClass)
                    ? metadata.PresentClass
                    : config.PresentClass.Trim(),
                AbsentClass = string.IsNullOrWhiteSpace(config.AbsentClass)
                    ? metadata.AbsentClass
                    : config.AbsentClass.Trim(),
                ProbabilityThreshold = config.ProbabilityThreshold > 0f && config.ProbabilityThreshold < 1f
                    ? config.ProbabilityThreshold
                    : metadata.ProbabilityThreshold,
                TensorRtCacheKey = cacheKey
            };
        }
        catch (JsonException)
        {
            return metadata;
        }
    }
}

public sealed class OcrPipelineMetadata
{
    public string DetPath { get; init; } = string.Empty;
    public string ClsPath { get; init; } = string.Empty;
    public string RecPath { get; init; } = string.Empty;
    public string DictPath { get; init; } = string.Empty;
    public bool DoAngle { get; init; }
    public bool ReturnWordBox { get; init; }
}
