namespace VideoInferenceDemo;

public sealed class InspectionTriggerEventArgs : EventArgs
{
    public required string Source { get; init; }

    public string? StationId { get; init; }

    public string? TriggerId { get; init; }

    public string? ProductModel { get; init; }

    public string? TaskId { get; init; }

    public string? PositionNo { get; init; }

    public DateTimeOffset TriggerTime { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyDictionary<string, string> Payload { get; init; } = new Dictionary<string, string>();
}
