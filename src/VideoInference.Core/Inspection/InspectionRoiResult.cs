using System.Text.Json.Serialization;

namespace VideoInferenceDemo;

public sealed record InspectionRoiResult
{
    public required string RoiId { get; init; }

    public required string RoiName { get; init; }

    public string? ModelId { get; init; }

    public InspectionCycleDecision Decision { get; init; } = InspectionCycleDecision.Unknown;

    public double? Score { get; init; }

    public IReadOnlyList<InspectionFinding> Findings { get; init; } = Array.Empty<InspectionFinding>();

    public IReadOnlyDictionary<string, string> Metrics { get; init; } = new Dictionary<string, string>();

    public int? DefectComponentCount { get; init; }

    public double? DefectMaxAreaPx { get; init; }

    public double? DefectMaxPerimeterPx { get; init; }

    public double? DefectMaxAreaPerimeterRatio { get; init; }

    public string? DefectSummaryText { get; init; }

    public string? DefectComponentsText { get; init; }

    [JsonIgnore]
    public byte[]? SegmentationMask { get; init; }

    [JsonIgnore]
    public int SegmentationMaskWidth { get; init; }

    [JsonIgnore]
    public int SegmentationMaskHeight { get; init; }
}
