namespace VideoInferenceDemo;

public sealed record InspectionFinding
{
    public required string Code { get; init; }

    public required string Message { get; init; }

    public InspectionCycleDecision Severity { get; init; } = InspectionCycleDecision.Ng;
}
