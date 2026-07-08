namespace VideoInferenceDemo;

public sealed record VisionWorkspaceActivationResult(
    VisionWorkspaceSnapshot WorkspaceSnapshot,
    ModelActivationAttemptResult Attempt);
