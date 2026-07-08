namespace VideoInferenceDemo;

internal sealed record PipelineExecutionMetadata(
    string SessionId,
    string SourceId,
    VideoSourceType SourceType,
    string RunUuid,
    long RunStartedUtcMs,
    string? ModelVersion);
