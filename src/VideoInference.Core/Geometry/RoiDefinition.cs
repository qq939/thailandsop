namespace VideoInferenceDemo;

public sealed record RoiDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? CameraId { get; init; }

    public double CenterX { get; init; } = 0.5;

    public double CenterY { get; init; } = 0.5;

    public double Width { get; init; } = 0.25;

    public double Height { get; init; } = 0.25;

    public double AngleDeg { get; init; }

    public bool Enabled { get; init; } = true;

    public string? ModelId { get; init; }

    public int SortOrder { get; init; }

    public RoiCoordinateSpace CoordinateSpace { get; init; } = RoiCoordinateSpace.Normalized;
}
