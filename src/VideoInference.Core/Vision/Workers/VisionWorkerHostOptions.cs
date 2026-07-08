using System.Collections.ObjectModel;

namespace VideoInferenceDemo;

public sealed class VisionWorkerHostOptions
{
    public required string TaskId { get; init; }
    public required VisionTaskKind TaskKind { get; init; }
    public required VisionRuntimeKind RuntimeKind { get; init; }
    public required string WorkerKind { get; init; }
    public required VisionWorkerProtocolKind ProtocolKind { get; init; }
    public required string EndpointName { get; init; }
    public required WorkerProcessStartSpec ProcessStart { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
}
