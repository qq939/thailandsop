using OpenCvSharp;

namespace VideoInferenceDemo;

internal sealed record PipelineFrameExecutionRequest(
    FramePacket Packet,
    Mat Image,
    IVisionTask PrimaryTask,
    IReadOnlyList<IVisionTask> SidecarTasks,
    VisionTaskExecutionContext ExecutionContext,
    PipelineExecutionMetadata Metadata);
