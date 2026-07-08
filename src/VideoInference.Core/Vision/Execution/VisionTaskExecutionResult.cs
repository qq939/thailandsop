using OpenCvSharp;

namespace VideoInferenceDemo;

public sealed class VisionTaskExecutionResult
{
    public required VisionTaskPayload Payload { get; init; }
    public required Action<Mat> Annotate { get; init; }
    public string? DeviceLabel { get; init; }
    public IReadOnlyDictionary<string, string> Metrics { get; init; } = new Dictionary<string, string>();
}
