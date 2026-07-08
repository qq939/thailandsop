using System.Collections.ObjectModel;

namespace VideoInferenceDemo;

public sealed class VisionWorkerRequest
{
    public required string RequestId { get; init; }
    public required long FrameId { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required VisionTaskKind TaskKind { get; init; }
    public required VisionWorkerImageFrame Frame { get; init; }
    public VisionWorkerRegion? Roi { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
}
