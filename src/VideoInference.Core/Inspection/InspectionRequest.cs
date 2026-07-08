using OpenCvSharp;

namespace VideoInferenceDemo;

public sealed record InspectionRequest
{
    public required Mat OriginalImage { get; init; }

    public required string ProductModel { get; init; }

    public required string TaskId { get; init; }

    public required string PositionNo { get; init; }

    public string? StationId { get; init; }

    public string? TaskInstanceId { get; init; }

    public string? CameraId { get; init; }

    public string? ActionType { get; init; }

    public string? TriggerId { get; init; }

    public DateTimeOffset TriggerTime { get; init; } = DateTimeOffset.UtcNow;

    public InspectionOperatorSnapshot? Operator { get; init; }

    public InspectionRecipeKey RecipeKey => new(ProductModel, TaskId, PositionNo);
}
