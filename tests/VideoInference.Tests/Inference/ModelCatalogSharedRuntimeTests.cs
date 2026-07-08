using Xunit;

namespace VideoInferenceDemo.Tests.Inference;

[Collection("DbSession")]
public sealed class ModelCatalogSharedRuntimeTests
{
    [Fact]
    public void Discover_ParsesSharedRuntimeFlag()
    {
        using var context = new DesktopCoordinatorTestContext();
        var bundleDir = Path.Combine(context.RootDirectory, "DL", "shared-model");
        Directory.CreateDirectory(bundleDir);
        File.WriteAllBytes(Path.Combine(bundleDir, "model.onnx"), new byte[] { 0 });
        File.WriteAllText(
            Path.Combine(bundleDir, "model.json"),
            """
            {
              "id": "shared-model",
              "displayName": "Shared Model",
              "taskType": "detection",
              "modelFile": "model.onnx",
              "shared": true
            }
            """);

        var model = Assert.Single(ModelCatalog.Discover(Path.Combine(context.RootDirectory, "DL")));
        Assert.True(model.IsSharedRuntime);
    }
}
