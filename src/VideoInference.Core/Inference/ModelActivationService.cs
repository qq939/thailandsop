using System.IO;

namespace VideoInferenceDemo;

public sealed class ModelActivationService
{
    public ModelActivationResult CreateModelBinding(
        ModelCatalogEntry model,
        InferenceDeviceKind deviceKind,
        float confidenceThreshold,
        float nmsThreshold)
    {
        ArgumentNullException.ThrowIfNull(model);

        var plan = ModelBindingPlanFactory.Create(model);
        _ = ModelPipelineFactory.CreateDescriptor(plan, deviceKind, confidenceThreshold, nmsThreshold);
        var binding = PrimaryVisionTaskBinding.ForModel(
            plan,
            new VisionTaskCreationContext(deviceKind, confidenceThreshold, nmsThreshold));

        return new ModelActivationResult(binding);
    }

    public ModelActivationAttemptResult TryCreateModelBinding(
        ModelCatalogEntry? model,
        InferenceDeviceKind deviceKind,
        float confidenceThreshold,
        float nmsThreshold)
    {
        if (model == null)
        {
            return new ModelActivationAttemptResult(
                false,
                null,
                ModelActivationState.NoModel);
        }

        if (!File.Exists(model.ModelPath))
        {
            return new ModelActivationAttemptResult(
                false,
                null,
                ModelActivationState.Error,
                $"Model file not found: {model.ModelPath}");
        }

        try
        {
            var activation = CreateModelBinding(model, deviceKind, confidenceThreshold, nmsThreshold);
            return new ModelActivationAttemptResult(
                true,
                activation,
                ModelActivationState.ModelSelected);
        }
        catch (Exception ex)
        {
            return new ModelActivationAttemptResult(
                false,
                null,
                ModelActivationState.Error,
                $"Model load failed: {ex.Message}",
                ex);
        }
    }
}
