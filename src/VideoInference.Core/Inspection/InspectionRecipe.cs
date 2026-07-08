namespace VideoInferenceDemo;

public sealed record InspectionRecipe
{
    public required InspectionRecipeKey Key { get; init; }

    public CalibrationContext Calibration { get; init; } = CalibrationContext.Empty;

    public IReadOnlyList<RoiDefinition> Rois { get; init; } = Array.Empty<RoiDefinition>();

    public IReadOnlyList<InspectionModelReference> ModelBindings { get; init; } = Array.Empty<InspectionModelReference>();

    public IReadOnlyDictionary<string, CameraAlignmentDefinition> AlignmentByCameraId { get; init; } =
        new Dictionary<string, CameraAlignmentDefinition>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
}
