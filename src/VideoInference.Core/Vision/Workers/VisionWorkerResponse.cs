using System.Collections.ObjectModel;

namespace VideoInferenceDemo;

public sealed class VisionWorkerResponse
{
    public required string RequestId { get; init; }
    public required long FrameId { get; init; }
    public required bool IsSuccess { get; init; }
    public string? RuntimeLabel { get; init; }
    public string? PayloadJson { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
}
