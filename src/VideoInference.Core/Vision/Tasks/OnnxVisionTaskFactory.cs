namespace VideoInferenceDemo;

public sealed class OnnxVisionTaskFactory : IVisionTaskFactory
{
    public static OnnxVisionTaskFactory Instance { get; } = new();

    public bool CanCreate(VisionTaskDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.RuntimeKind == VisionRuntimeKind.OnnxRuntime &&
               (definition.TaskKind == VisionTaskKind.Detection ||
                definition.TaskKind == VisionTaskKind.ObbDetection ||
                definition.TaskKind == VisionTaskKind.Segmentation ||
                definition.TaskKind == VisionTaskKind.UnetSegmentation ||
                definition.TaskKind == VisionTaskKind.PresenceClassification ||
                definition.TaskKind == VisionTaskKind.SequenceBands);
    }

    public IVisionTask Create(VisionTaskDefinition definition, VisionTaskCreationContext context)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!CanCreate(definition))
        {
            throw new NotSupportedException(
                $"Vision task '{definition.Id}' with runtime '{definition.RuntimeKind}' and kind '{definition.TaskKind}' is not supported by {nameof(OnnxVisionTaskFactory)}.");
        }

        var model = ResolveModel(definition);
        var plan = ModelBindingPlanFactory.Create(model);
        var descriptor = ModelPipelineFactory.CreateDescriptor(
            plan,
            context.OnnxDeviceKind,
            context.ConfidenceThreshold,
            context.NmsThreshold);

        return descriptor.ModelKind switch
        {
            InferenceModelKind.YoloDetection => new YoloVisionTask(
                definition.Id,
                descriptor.ModelPath,
                descriptor.DeviceKind,
                descriptor.ConfidenceThreshold,
                descriptor.NmsThreshold,
                descriptor.ClassNames,
                descriptor.YoloMetadata),
            InferenceModelKind.YoloObbDetection => new YoloObbVisionTask(
                definition.Id,
                descriptor.ModelPath,
                descriptor.DeviceKind,
                descriptor.ConfidenceThreshold,
                descriptor.NmsThreshold,
                descriptor.ClassNames,
                descriptor.YoloObbMetadata),
            InferenceModelKind.YoloSegmentation => new YoloSegmentationVisionTask(
                definition.Id,
                descriptor.ModelPath,
                descriptor.DeviceKind,
                descriptor.ConfidenceThreshold,
                descriptor.NmsThreshold,
                descriptor.ClassNames,
                descriptor.YoloSegmentationMetadata),
            InferenceModelKind.UnetSegmentation => new UnetSegmentationVisionTask(
                definition.Id,
                descriptor.ModelPath,
                descriptor.DeviceKind,
                descriptor.ClassNames,
                descriptor.UnetSegmentationMetadata),
            InferenceModelKind.PresenceClassification => new PresenceClassificationVisionTask(
                definition.Id,
                descriptor.ModelPath,
                descriptor.DeviceKind,
                descriptor.ClassNames,
                descriptor.PresenceClassificationMetadata),
            InferenceModelKind.SequenceBands when descriptor.SequenceMetadata != null => new SequenceVisionTask(
                definition.Id,
                descriptor.ModelPath,
                descriptor.DeviceKind,
                descriptor.SequenceMetadata),
            _ => throw new NotSupportedException(
                $"Descriptor model kind '{descriptor.ModelKind}' is not supported by {nameof(OnnxVisionTaskFactory)}.")
        };
    }

    private static ModelCatalogEntry ResolveModel(VisionTaskDefinition definition)
    {
        var modelPath = TryGetRequiredMetadata(definition, "modelPath");
        var bundleDirectory = TryGetRequiredMetadata(definition, "bundleDirectory");
        var classConfigPath = TryGetOptionalMetadata(definition, "classConfigPath");
        var taskType = definition.TaskKind switch
        {
            VisionTaskKind.SequenceBands => ModelTaskType.SequenceBands,
            VisionTaskKind.ObbDetection => ModelTaskType.ObbDetection,
            VisionTaskKind.Segmentation => ModelTaskType.Segmentation,
            VisionTaskKind.UnetSegmentation => ModelTaskType.UnetSegmentation,
            VisionTaskKind.PresenceClassification => ModelTaskType.PresenceClassification,
            VisionTaskKind.OcrText => ModelTaskType.OcrText,
            _ => ModelTaskType.Detection
        };

        var yoloMetadata = definition.TaskKind == VisionTaskKind.Detection
            ? BuildYoloMetadata(definition)
            : null;
        var yoloObbMetadata = definition.TaskKind == VisionTaskKind.ObbDetection
            ? BuildYoloObbMetadata(definition)
            : null;
        var yoloSegmentationMetadata = definition.TaskKind == VisionTaskKind.Segmentation
            ? BuildYoloSegmentationMetadata(definition)
            : null;
        var unetSegmentationMetadata = definition.TaskKind == VisionTaskKind.UnetSegmentation
            ? BuildUnetSegmentationMetadata(definition)
            : null;
        var presenceClassificationMetadata = definition.TaskKind == VisionTaskKind.PresenceClassification
            ? BuildPresenceClassificationMetadata(definition)
            : null;

        return new ModelCatalogEntry
        {
            Id = definition.Id,
            DisplayName = definition.DisplayName,
            Description = definition.DisplayName,
            BundleDirectory = bundleDirectory,
            ModelPath = modelPath,
            ClassConfigPath = classConfigPath,
            TaskType = taskType,
            IsLegacyRoot = string.Equals(TryGetOptionalMetadata(definition, "isLegacyRoot"), "true", StringComparison.OrdinalIgnoreCase),
            YoloMetadata = yoloMetadata,
            YoloObbMetadata = yoloObbMetadata,
            YoloSegmentationMetadata = yoloSegmentationMetadata,
            UnetSegmentationMetadata = unetSegmentationMetadata,
            PresenceClassificationMetadata = presenceClassificationMetadata
        };
    }

    private static YoloDetectionMetadata? BuildYoloMetadata(VisionTaskDefinition definition)
    {
        var outputLayoutRaw = TryGetOptionalMetadata(definition, "yolo.outputLayout");
        var scoreModeRaw = TryGetOptionalMetadata(definition, "yolo.scoreMode");
        var classCountRaw = TryGetOptionalMetadata(definition, "yolo.classCount");
        var minScoreRaw = TryGetOptionalMetadata(definition, "yolo.minScore");
        var nmsThresholdRaw = TryGetOptionalMetadata(definition, "yolo.nmsThreshold");

        if (string.IsNullOrWhiteSpace(outputLayoutRaw) &&
            string.IsNullOrWhiteSpace(scoreModeRaw) &&
            string.IsNullOrWhiteSpace(classCountRaw))
        {
            return null;
        }

        int? classCount = null;
        if (int.TryParse(classCountRaw, out var parsedClassCount) && parsedClassCount > 0)
        {
            classCount = parsedClassCount;
        }

        return new YoloDetectionMetadata
        {
            OutputLayout = Enum.TryParse<YoloOutputLayout>(outputLayoutRaw, ignoreCase: true, out var outputLayout)
                ? outputLayout
                : YoloOutputLayout.Auto,
            ScoreMode = Enum.TryParse<YoloScoreMode>(scoreModeRaw, ignoreCase: true, out var scoreMode)
                ? scoreMode
                : YoloScoreMode.Auto,
            ClassCount = classCount,
            MinScore = ParseThreshold(minScoreRaw),
            NmsThreshold = ParseThreshold(nmsThresholdRaw),
            TensorRtCacheKey = TryGetOptionalMetadata(definition, "yolo.tensorRtCacheKey")
        };
    }

    private static YoloSegmentationMetadata? BuildYoloSegmentationMetadata(VisionTaskDefinition definition)
    {
        var outputLayoutRaw = TryGetOptionalMetadata(definition, "yolo.outputLayout");
        var scoreModeRaw = TryGetOptionalMetadata(definition, "yolo.scoreMode");
        var classCountRaw = TryGetOptionalMetadata(definition, "yolo.classCount");
        var detectionOutputName = TryGetOptionalMetadata(definition, "yolo.detectionOutputName");
        var prototypeOutputName = TryGetOptionalMetadata(definition, "yolo.prototypeOutputName");
        var maskThresholdRaw = TryGetOptionalMetadata(definition, "yolo.maskThreshold");
        var minScoreRaw = TryGetOptionalMetadata(definition, "yolo.minScore");
        var nmsThresholdRaw = TryGetOptionalMetadata(definition, "yolo.nmsThreshold");

        int? classCount = null;
        if (int.TryParse(classCountRaw, out var parsedClassCount) && parsedClassCount > 0)
        {
            classCount = parsedClassCount;
        }

        var maskThreshold = 0.5f;
        if (float.TryParse(maskThresholdRaw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedMaskThreshold) &&
            parsedMaskThreshold > 0f &&
            parsedMaskThreshold < 1f)
        {
            maskThreshold = parsedMaskThreshold;
        }

        return new YoloSegmentationMetadata
        {
            OutputLayout = Enum.TryParse<YoloOutputLayout>(outputLayoutRaw, ignoreCase: true, out var outputLayout)
                ? outputLayout
                : YoloOutputLayout.Auto,
            ScoreMode = Enum.TryParse<YoloScoreMode>(scoreModeRaw, ignoreCase: true, out var scoreMode)
                ? scoreMode
                : YoloScoreMode.Auto,
            ClassCount = classCount,
            DetectionOutputName = detectionOutputName,
            PrototypeOutputName = prototypeOutputName,
            MinScore = ParseThreshold(minScoreRaw),
            NmsThreshold = ParseThreshold(nmsThresholdRaw),
            MaskThreshold = maskThreshold,
            TensorRtCacheKey = TryGetOptionalMetadata(definition, "yolo.tensorRtCacheKey")
        };
    }

    private static YoloObbDetectionMetadata? BuildYoloObbMetadata(VisionTaskDefinition definition)
    {
        var outputLayoutRaw = TryGetOptionalMetadata(definition, "yolo.outputLayout");
        var scoreModeRaw = TryGetOptionalMetadata(definition, "yolo.scoreMode");
        var classCountRaw = TryGetOptionalMetadata(definition, "yolo.classCount");
        var minScoreRaw = TryGetOptionalMetadata(definition, "yolo.minScore");
        var nmsThresholdRaw = TryGetOptionalMetadata(definition, "yolo.nmsThreshold");
        var locatorMinScoreRaw = TryGetOptionalMetadata(definition, "locator.minScore");

        int? classCount = null;
        if (int.TryParse(classCountRaw, out var parsedClassCount) && parsedClassCount > 0)
        {
            classCount = parsedClassCount;
        }

        var locatorMinScore = 0f;
        if (float.TryParse(
                locatorMinScoreRaw,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsedMinScore) &&
            parsedMinScore > 0f &&
            parsedMinScore < 1f)
        {
            locatorMinScore = parsedMinScore;
        }

        return new YoloObbDetectionMetadata
        {
            OutputLayout = Enum.TryParse<YoloOutputLayout>(outputLayoutRaw, ignoreCase: true, out var outputLayout)
                ? outputLayout
                : YoloOutputLayout.Auto,
            ScoreMode = Enum.TryParse<YoloScoreMode>(scoreModeRaw, ignoreCase: true, out var scoreMode)
                ? scoreMode
                : YoloScoreMode.Auto,
            ClassCount = classCount,
            MinScore = ParseThreshold(minScoreRaw),
            NmsThreshold = ParseThreshold(nmsThresholdRaw),
            LocatorMinScore = locatorMinScore,
            TensorRtCacheKey = TryGetOptionalMetadata(definition, "yolo.tensorRtCacheKey")
        };
    }

    private static UnetSegmentationMetadata BuildUnetSegmentationMetadata(VisionTaskDefinition definition)
    {
        return new UnetSegmentationMetadata
        {
            ProbabilityThreshold = ParseThreshold(TryGetOptionalMetadata(definition, "unet.probabilityThreshold"), 0.6f),
            MinComponentArea = ParseNonNegativeInt(TryGetOptionalMetadata(definition, "unet.minComponentArea"), 20),
            MinComponentPerimeter = ParseNonNegativeFloat(TryGetOptionalMetadata(definition, "unet.minComponentPerimeter")),
            MinAreaPerimeterRatio = ParseNonNegativeFloat(TryGetOptionalMetadata(definition, "unet.minAreaPerimeterRatio")),
            MaxAreaPerimeterRatio = ParseNonNegativeFloat(TryGetOptionalMetadata(definition, "unet.maxAreaPerimeterRatio")),
            TensorRtCacheKey = TryGetOptionalMetadata(definition, "unet.tensorRtCacheKey")
        };
    }

    private static PresenceClassificationMetadata BuildPresenceClassificationMetadata(VisionTaskDefinition definition)
    {
        return new PresenceClassificationMetadata
        {
            PresentClass = ResolveText(TryGetOptionalMetadata(definition, "classification.presentClass"), "OK"),
            AbsentClass = ResolveText(TryGetOptionalMetadata(definition, "classification.absentClass"), "NG"),
            ProbabilityThreshold = ParseThreshold(TryGetOptionalMetadata(definition, "classification.probabilityThreshold"), 0.5f),
            TensorRtCacheKey = TryGetOptionalMetadata(definition, "classification.tensorRtCacheKey")
        };
    }

    private static string ResolveText(string? raw, string fallback)
    {
        return string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim();
    }

    private static float ParseThreshold(string? raw, float fallback)
    {
        return float.TryParse(
                   raw,
                   System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out var parsed) &&
               parsed > 0f &&
               parsed < 1f
            ? parsed
            : fallback;
    }

    private static int ParseNonNegativeInt(string? raw, int fallback)
    {
        return int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(0, parsed)
            : fallback;
    }

    private static float ParseNonNegativeFloat(string? raw)
    {
        return float.TryParse(
                   raw,
                   System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out var parsed)
            ? Math.Max(0f, parsed)
            : 0f;
    }

    private static float ParseThreshold(string? raw)
    {
        return float.TryParse(
                   raw,
                   System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out var parsed) &&
               parsed > 0f &&
               parsed < 1f
            ? parsed
            : 0f;
    }

    private static string TryGetRequiredMetadata(VisionTaskDefinition definition, string key)
    {
        var value = TryGetOptionalMetadata(definition, key);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException(
            $"Vision task '{definition.Id}' is missing required metadata '{key}'.");
    }

    private static string TryGetOptionalMetadata(VisionTaskDefinition definition, string key)
    {
        return definition.Metadata.TryGetValue(key, out var value) ? value : string.Empty;
    }
}
