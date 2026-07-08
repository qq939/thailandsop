namespace VideoInferenceDemo;

public enum WorkspaceSelectionMode
{
    Auto,
    PrimaryTask,
    ModelBackedPrimaryTask
}

public sealed record WorkspaceSelectionMaterializationResult(
    bool Success,
    SessionStartPrecheckState FailureState,
    VisionWorkspaceSnapshot WorkspaceSnapshot,
    PrimaryVisionTaskBinding? ActivePrimaryBinding,
    ModelActivationAttemptResult? ModelBackedActivationAttempt,
    string StatusText,
    string InferenceStatus,
    string InferenceDeviceText,
    string? LastError = null)
{
    public SessionStartPrecheckResult ToPrecheckResult()
    {
        return Success
            ? SessionStartPrecheckResult.Success
            : new SessionStartPrecheckResult(
                FailureState,
                !string.IsNullOrWhiteSpace(LastError) ? LastError : InferenceStatus);
    }
}
