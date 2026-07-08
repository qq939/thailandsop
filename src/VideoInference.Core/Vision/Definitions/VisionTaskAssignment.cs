namespace VideoInferenceDemo;

public sealed record VisionTaskAssignment(
    string SessionId,
    string TaskId,
    bool IsPrimary,
    int ExecutionOrder,
    bool IsEnabled);
