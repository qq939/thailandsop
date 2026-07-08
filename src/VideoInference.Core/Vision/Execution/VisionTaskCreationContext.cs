namespace VideoInferenceDemo;

public sealed record VisionTaskCreationContext(
    InferenceDeviceKind OnnxDeviceKind,
    float ConfidenceThreshold,
    float NmsThreshold);
