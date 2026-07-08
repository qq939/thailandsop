namespace VideoInferenceDemo;

public sealed record VisionFrameResult(
    FrameEntity Frame,
    string SessionId,
    string TaskId,
    VisionTaskKind TaskKind,
    VisionTaskPayload Payload);
