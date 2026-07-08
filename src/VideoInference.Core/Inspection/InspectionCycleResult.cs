namespace VideoInferenceDemo;

public sealed record InspectionCycleResult
{
    public required InspectionRecipeKey RecipeKey { get; init; }

    public string? StationId { get; init; }

    public string? TaskInstanceId { get; init; }

    public string? CameraId { get; init; }

    public string? ActionType { get; init; }

    public string? TriggerId { get; init; }

    public DateTimeOffset TriggerTime { get; init; }

    public InspectionOperatorSnapshot? Operator { get; init; }

    public InspectionCycleDecision Decision { get; init; } = InspectionCycleDecision.Unknown;

    public string SummaryMessage { get; init; } = string.Empty;

    public CalibrationContext Calibration { get; init; } = CalibrationContext.Empty;

    public IReadOnlyList<RoiDefinition> ResolvedRois { get; init; } = Array.Empty<RoiDefinition>();

    public IReadOnlyList<InspectionModelReference> ResolvedModels { get; init; } = Array.Empty<InspectionModelReference>();

    public IReadOnlyList<InspectionRoiResult> RoiResults { get; init; } = Array.Empty<InspectionRoiResult>();

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
