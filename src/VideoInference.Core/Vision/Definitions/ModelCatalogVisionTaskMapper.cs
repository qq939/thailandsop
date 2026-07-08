using System.Collections.ObjectModel;

namespace VideoInferenceDemo;

public static class ModelCatalogVisionTaskMapper
{
    public static VisionTaskDefinition ToVisionTaskDefinition(ModelCatalogEntry model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["modelPath"] = model.ModelPath,
            ["bundleDirectory"] = model.BundleDirectory,
            ["classConfigPath"] = model.ClassConfigPath,
            ["modelFileName"] = model.ModelFileName,
            ["taskType"] = model.TaskType.ToString(),
            ["taskTypeDisplay"] = model.TaskTypeDisplay,
            ["inputWidth"] = model.InputWidth.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["inputHeight"] = model.InputHeight.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["isLegacyRoot"] = model.IsLegacyRoot ? "true" : "false",
            ["shared"] = model.IsSharedRuntime ? "true" : "false"
        };

        if (model.TaskType == ModelTaskType.OcrText)
        {
            if (!string.IsNullOrWhiteSpace(model.DictPath))
            {
                metadata["dictPath"] = model.DictPath;
            }

            metadata["fixedWidth"] = model.InputWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["fixedHeight"] = model.InputHeight.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else if (model.TaskType == ModelTaskType.OcrPipeline && model.OcrPipelineMetadata != null)
        {
            metadata["ocr.detPath"] = model.OcrPipelineMetadata.DetPath;
            metadata["ocr.clsPath"] = model.OcrPipelineMetadata.ClsPath;
            metadata["ocr.recPath"] = model.OcrPipelineMetadata.RecPath;
            metadata["ocr.dictPath"] = model.OcrPipelineMetadata.DictPath;
            metadata["ocr.doAngle"] = model.OcrPipelineMetadata.DoAngle ? "true" : "false";
            metadata["ocr.returnWordBox"] = model.OcrPipelineMetadata.ReturnWordBox ? "true" : "false";
        }

        if (model.YoloMetadata?.ClassCount is int classCount)
        {
            metadata["yolo.classCount"] = classCount.ToString();
        }
        else if (model.YoloObbMetadata?.ClassCount is int obbClassCount)
        {
            metadata["yolo.classCount"] = obbClassCount.ToString();
        }
        else if (model.YoloSegmentationMetadata?.ClassCount is int segClassCount)
        {
            metadata["yolo.classCount"] = segClassCount.ToString();
        }

        metadata["yolo.outputLayout"] = (model.YoloMetadata?.OutputLayout ?? model.YoloObbMetadata?.OutputLayout ?? model.YoloSegmentationMetadata?.OutputLayout)?.ToString() ?? string.Empty;
        metadata["yolo.scoreMode"] = (model.YoloMetadata?.ScoreMode ?? model.YoloObbMetadata?.ScoreMode ?? model.YoloSegmentationMetadata?.ScoreMode)?.ToString() ?? string.Empty;
        metadata["yolo.minScore"] = (model.YoloMetadata?.MinScore ?? model.YoloObbMetadata?.MinScore ?? model.YoloSegmentationMetadata?.MinScore)?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        metadata["yolo.nmsThreshold"] = (model.YoloMetadata?.NmsThreshold ?? model.YoloObbMetadata?.NmsThreshold ?? model.YoloSegmentationMetadata?.NmsThreshold)?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        metadata["yolo.detectionOutputName"] = model.YoloSegmentationMetadata?.DetectionOutputName ?? string.Empty;
        metadata["yolo.prototypeOutputName"] = model.YoloSegmentationMetadata?.PrototypeOutputName ?? string.Empty;
        metadata["yolo.maskThreshold"] = model.YoloSegmentationMetadata?.MaskThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        metadata["locator.minScore"] = model.YoloObbMetadata?.LocatorMinScore.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        metadata["yolo.tensorRtCacheKey"] = model.Id;

        if (model.UnetSegmentationMetadata != null)
        {
            metadata["unet.probabilityThreshold"] = model.UnetSegmentationMetadata.ProbabilityThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["unet.minComponentArea"] = model.UnetSegmentationMetadata.MinComponentArea.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["unet.minComponentPerimeter"] = model.UnetSegmentationMetadata.MinComponentPerimeter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["unet.minAreaPerimeterRatio"] = model.UnetSegmentationMetadata.MinAreaPerimeterRatio.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["unet.maxAreaPerimeterRatio"] = model.UnetSegmentationMetadata.MaxAreaPerimeterRatio.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["unet.tensorRtCacheKey"] = model.Id;
        }

        if (model.PresenceClassificationMetadata != null)
        {
            metadata["classification.presentClass"] = model.PresenceClassificationMetadata.PresentClass;
            metadata["classification.absentClass"] = model.PresenceClassificationMetadata.AbsentClass;
            metadata["classification.probabilityThreshold"] = model.PresenceClassificationMetadata.ProbabilityThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["classification.tensorRtCacheKey"] = model.Id;
        }

        return new VisionTaskDefinition
        {
            Id = model.Id,
            DisplayName = model.DisplayName,
            TaskKind = MapTaskKind(model.TaskType),
            RuntimeKind = model.TaskType is ModelTaskType.OcrText or ModelTaskType.OcrPipeline
                ? VisionRuntimeKind.OcrRuntime
                : VisionRuntimeKind.OnnxRuntime,
            BundlePath = model.BundleDirectory,
            ConfigPath = model.ClassConfigPath,
            Metadata = new ReadOnlyDictionary<string, string>(metadata)
        };
    }

    public static IReadOnlyList<VisionTaskDefinition> ToVisionTaskDefinitions(IEnumerable<ModelCatalogEntry> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        return models.Select(ToVisionTaskDefinition).ToArray();
    }

    private static VisionTaskKind MapTaskKind(ModelTaskType taskType)
    {
        return taskType switch
        {
            ModelTaskType.UnetSegmentation => VisionTaskKind.UnetSegmentation,
            ModelTaskType.PresenceClassification => VisionTaskKind.PresenceClassification,
            ModelTaskType.Segmentation => VisionTaskKind.Segmentation,
            ModelTaskType.ObbDetection => VisionTaskKind.ObbDetection,
            ModelTaskType.SequenceBands => VisionTaskKind.SequenceBands,
            ModelTaskType.OcrText => VisionTaskKind.OcrText,
            ModelTaskType.OcrPipeline => VisionTaskKind.OcrPipeline,
            _ => VisionTaskKind.Detection
        };
    }
}
