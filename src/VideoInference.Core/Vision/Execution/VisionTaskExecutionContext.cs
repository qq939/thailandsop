namespace VideoInferenceDemo;

public sealed record VisionTaskExecutionContext(
    SessionFrameContext Frame,
    VisionTaskRenderStyle RenderStyle);
