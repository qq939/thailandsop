namespace VideoInferenceDemo;

public sealed class VisionWorkerImageFrame
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Stride { get; init; }
    public required string PixelFormat { get; init; }
    public required byte[] ImageBytes { get; init; }
}
