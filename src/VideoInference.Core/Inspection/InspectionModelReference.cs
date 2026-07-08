namespace VideoInferenceDemo;

public sealed record InspectionModelReference
{
    public required string ModelId { get; init; }

    public string? Alias { get; init; }

    public IReadOnlyList<string> RoiIds { get; init; } = Array.Empty<string>();

    public int Sequence { get; init; }

    public string? DependsOnAlias { get; init; }

    public string EffectiveAlias => string.IsNullOrWhiteSpace(Alias) ? ModelId : Alias;
}
