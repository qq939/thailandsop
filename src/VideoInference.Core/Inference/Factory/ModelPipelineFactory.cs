using System;
using System.IO;

namespace VideoInferenceDemo;

public static class ModelPipelineFactory
{
    public static InferenceModelDescriptor CreateDescriptor(
        ModelBindingPlan plan,
        InferenceDeviceKind onnxDeviceKind,
        float confidenceThreshold,
        float nmsThreshold)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(plan.Model);

        var model = plan.Model;
        if (model.TaskType == ModelTaskType.OcrPipeline)
        {
            throw new InvalidOperationException("OCR pipeline models are supported by ImageInspection only.");
        }

        if (model.TaskType == ModelTaskType.SequenceBands)
        {
            if (!SequenceModelMetadata.TryLoadForModel(model.ModelPath, out var sequenceMetadata))
            {
                throw new InvalidOperationException("Sequence model metadata is missing or invalid in model.json.");
            }

            return new InferenceModelDescriptor
            {
                Model = model,
                ModelKind = InferenceModelKind.SequenceBands,
                DeviceKind = onnxDeviceKind,
                ModelPath = model.ModelPath,
                SequenceMetadata = sequenceMetadata
            };
        }

        var ext = Path.GetExtension(model.ModelPath).ToLowerInvariant();
        if (ext == ".onnx" && model.TaskType == ModelTaskType.UnetSegmentation)
        {
            return new InferenceModelDescriptor
            {
                Model = model,
                ModelKind = InferenceModelKind.UnetSegmentation,
                DeviceKind = onnxDeviceKind,
                ModelPath = model.ModelPath,
                ClassNames = plan.ClassNames,
                UnetSegmentationMetadata = model.UnetSegmentationMetadata ?? UnetSegmentationMetadata.Default
            };
        }

        if (ext == ".onnx" && model.TaskType == ModelTaskType.PresenceClassification)
        {
            return new InferenceModelDescriptor
            {
                Model = model,
                ModelKind = InferenceModelKind.PresenceClassification,
                DeviceKind = onnxDeviceKind,
                ModelPath = model.ModelPath,
                ClassNames = plan.ClassNames,
                PresenceClassificationMetadata = model.PresenceClassificationMetadata ?? PresenceClassificationMetadata.Default
            };
        }

        if (ext == ".onnx" && model.TaskType == ModelTaskType.ObbDetection)
        {
            return new InferenceModelDescriptor
            {
                Model = model,
                ModelKind = InferenceModelKind.YoloObbDetection,
                DeviceKind = onnxDeviceKind,
                ModelPath = model.ModelPath,
                ClassNames = plan.ClassNames,
                ConfidenceThreshold = ResolveThreshold(model.YoloObbMetadata?.MinScore, confidenceThreshold),
                NmsThreshold = ResolveThreshold(model.YoloObbMetadata?.NmsThreshold, nmsThreshold),
                YoloObbMetadata = model.YoloObbMetadata
            };
        }

        if (ext == ".onnx" && model.TaskType == ModelTaskType.Segmentation)
        {
            return new InferenceModelDescriptor
            {
                Model = model,
                ModelKind = InferenceModelKind.YoloSegmentation,
                DeviceKind = onnxDeviceKind,
                ModelPath = model.ModelPath,
                ClassNames = plan.ClassNames,
                ConfidenceThreshold = ResolveThreshold(model.YoloSegmentationMetadata?.MinScore, confidenceThreshold),
                NmsThreshold = ResolveThreshold(model.YoloSegmentationMetadata?.NmsThreshold, nmsThreshold),
                YoloSegmentationMetadata = model.YoloSegmentationMetadata
            };
        }

        if (ext == ".onnx")
        {
            return new InferenceModelDescriptor
            {
                Model = model,
                ModelKind = InferenceModelKind.YoloDetection,
                DeviceKind = onnxDeviceKind,
                ModelPath = model.ModelPath,
                ClassNames = plan.ClassNames,
                ConfidenceThreshold = ResolveThreshold(model.YoloMetadata?.MinScore, confidenceThreshold),
                NmsThreshold = ResolveThreshold(model.YoloMetadata?.NmsThreshold, nmsThreshold),
                YoloMetadata = model.YoloMetadata
            };
        }

        if (ext == ".engine")
        {
            throw new InvalidOperationException(
                $"TensorRT engine bundles are no longer supported directly: {model.ModelPath}. Use an ONNX model with ONNX Runtime TensorRT provider instead.");
        }

        throw new InvalidOperationException($"Unsupported model file: {model.ModelPath}");
    }

    private static float ResolveThreshold(float? modelValue, float fallback)
    {
        return modelValue is > 0f and < 1f ? modelValue.Value : fallback;
    }
}
