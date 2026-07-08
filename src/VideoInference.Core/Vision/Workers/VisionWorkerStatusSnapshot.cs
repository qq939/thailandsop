namespace VideoInferenceDemo;

public sealed record VisionWorkerStatusSnapshot(
    string WorkerKind,
    string EndpointName,
    VisionWorkerState State,
    string? RuntimeLabel,
    int? ProcessId,
    int? ExitCode,
    string? LastError,
    string? LastStdErr);
