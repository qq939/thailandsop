namespace VideoInferenceDemo;

public sealed record CameraAlignmentDefinition
{
    public bool Enabled { get; init; }

    public string LocatorModelId { get; init; } = string.Empty;

    public int LocatorClassId { get; init; } = -1;

    public string LocatorClassName { get; init; } = string.Empty;

    public double CenterX { get; init; } = 0.5;

    public double CenterY { get; init; } = 0.5;

    public double Width { get; init; } = 0.25;

    public double Height { get; init; } = 0.25;

    public double AngleDeg { get; init; }
}
