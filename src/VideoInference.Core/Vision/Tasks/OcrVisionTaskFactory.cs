namespace VideoInferenceDemo;

public sealed class OcrVisionTaskFactory : IVisionTaskFactory
{
    public static OcrVisionTaskFactory Instance { get; } = new();

    public bool CanCreate(VisionTaskDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.RuntimeKind == VisionRuntimeKind.OcrRuntime &&
               definition.TaskKind == VisionTaskKind.OcrText;
    }

    public IVisionTask Create(VisionTaskDefinition definition, VisionTaskCreationContext context)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!CanCreate(definition))
        {
            throw new NotSupportedException(
                $"Vision task '{definition.Id}' with runtime '{definition.RuntimeKind}' and kind '{definition.TaskKind}' is not supported by {nameof(OcrVisionTaskFactory)}.");
        }

        var modelPath = GetRequiredMetadata(definition, "modelPath");
        var dictPath = GetRequiredMetadata(definition, "dictPath");

        var deviceKindRaw = GetOptionalMetadata(definition, "deviceKind");
        var deviceKind = InferenceDeviceKind.GpuCuda;
        if (!string.IsNullOrWhiteSpace(deviceKindRaw) &&
            Enum.TryParse<InferenceDeviceKind>(deviceKindRaw, ignoreCase: true, out var parsedDeviceKind))
        {
            deviceKind = parsedDeviceKind;
        }

        var roiX = ParseIntMetadata(definition, "roiX", 0);
        var roiY = ParseIntMetadata(definition, "roiY", 0);
        var roiWidth = ParseIntMetadata(definition, "roiWidth", 100);
        var roiHeight = ParseIntMetadata(definition, "roiHeight", 48);
        var fixedWidth = ParseRequiredPositiveIntMetadata(definition, "fixedWidth");
        var fixedHeight = ParseRequiredPositiveIntMetadata(definition, "fixedHeight");

        return new OcrVisionTask(
            definition.Id,
            modelPath,
            dictPath,
            deviceKind,
            roiX,
            roiY,
            roiWidth,
            roiHeight,
            fixedWidth,
            fixedHeight);
    }

    private static string GetRequiredMetadata(VisionTaskDefinition definition, string key)
    {
        var value = GetOptionalMetadata(definition, key);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException(
            $"Vision task '{definition.Id}' is missing required metadata '{key}'.");
    }

    private static string GetOptionalMetadata(VisionTaskDefinition definition, string key)
    {
        return definition.Metadata.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static int ParseIntMetadata(VisionTaskDefinition definition, string key, int defaultValue)
    {
        var raw = GetOptionalMetadata(definition, key);
        if (int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return defaultValue;
    }

    private static int ParseRequiredPositiveIntMetadata(VisionTaskDefinition definition, string key)
    {
        var raw = GetRequiredMetadata(definition, key);
        if (int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var value) &&
            value > 0)
        {
            return value;
        }

        throw new InvalidOperationException(
            $"Vision task '{definition.Id}' metadata '{key}' must be a positive integer.");
    }
}
