namespace VideoInferenceDemo;

public sealed record CalibrationContext
{
    public static CalibrationContext Empty { get; } = new()
    {
        CalibrationId = "default"
    };

    public required string CalibrationId { get; init; }

    public double? PixelsPerUnitX { get; init; }

    public double? PixelsPerUnitY { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
