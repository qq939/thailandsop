using System.Collections.ObjectModel;

namespace VideoInferenceDemo;

public sealed class VisionTaskDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required VisionTaskKind TaskKind { get; init; }
    public required VisionRuntimeKind RuntimeKind { get; init; }
    public string? BundlePath { get; init; }
    public string? ConfigPath { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
}
