using OpenCvSharp;

namespace VideoInferenceDemo;

public interface IVisionTask : IDisposable
{
    string TaskId { get; }
    VisionTaskKind TaskKind { get; }
    VisionRuntimeKind RuntimeKind { get; }
    string? ActiveDeviceLabel { get; }
    VisionTaskExecutionResult Execute(Mat image, VisionTaskExecutionContext context);
    void Warmup(int width, int height);
    void UpdateClassNames(string[]? classNames);
    bool TryHandleFailure(Exception ex, out string message);
}
