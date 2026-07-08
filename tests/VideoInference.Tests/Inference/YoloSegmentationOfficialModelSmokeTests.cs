using OpenCvSharp;
using Xunit;

namespace VideoInferenceDemo.Tests;

public sealed class YoloSegmentationOfficialModelSmokeTests
{
    [Fact]
    public void Yolo11mSegModel_CanRunThroughSegmentationTask()
    {
        var repoRoot = FindRepoRoot();
        var bundleDir = Path.Combine(repoRoot, "DL", "yolo11n_seg_official");
        var modelPath = Path.Combine(bundleDir, "yolo11m-seg-schaeffler.onnx");
        if (!File.Exists(modelPath))
        {
            return;
        }

        var model = ModelCatalog.Discover(Path.Combine(repoRoot, "DL"))
            .Single(item => item.Id == "yolo11m-schaeffler-seg");
        var definition = ModelCatalogVisionTaskMapper.ToVisionTaskDefinition(model);
        Assert.Equal(640, model.InputWidth);
        Assert.Equal(640, model.InputHeight);
        Assert.Equal("640", definition.Metadata["inputWidth"]);
        Assert.Equal("640", definition.Metadata["inputHeight"]);

        using var task = OnnxVisionTaskFactory.Instance.Create(
            definition,
            new VisionTaskCreationContext(InferenceDeviceKind.Cpu, 0.25f, 0.45f));

        using var image = new Mat(640, 640, MatType.CV_8UC3, Scalar.All(114));
        var result = task.Execute(image, new VisionTaskExecutionContext(
            new SessionFrameContext(0, 0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0, image.Width, image.Height),
            new VisionTaskRenderStyle(null, null, 2, 0.65)));

        Assert.Equal(VisionTaskKind.Segmentation, task.TaskKind);
        Assert.IsType<SegmentationPayload>(result.Payload);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "VideoInferenceDemo.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
