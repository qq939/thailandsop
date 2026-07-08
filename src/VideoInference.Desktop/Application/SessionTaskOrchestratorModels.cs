namespace VideoInferenceDemo;

public sealed record VisionTaskApplicationResult(
    PrimaryVisionTaskBinding Binding,
    string StatusText,
    string InferenceStatus,
    string InferenceDeviceText);

public sealed record SessionRebuildResult(CameraSessionViewModel? SelectedSession);
