using Microsoft.ML.OnnxRuntime;

namespace VideoInferenceDemo.Tests.Inference;

public sealed class OrtSessionFactoryTests
{
    [Fact]
    public void GetOptimizedModelPath_SkipsTensorRtProvider()
    {
        var modelPath = CreateTempModelFile();
        try
        {
            var options = new OrtSessionFactoryOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            var path = OrtSessionFactory.GetOptimizedModelPath(
                modelPath,
                OrtExecutionProviderKind.TensorRt,
                options);

            Assert.Null(path);
        }
        finally
        {
            File.Delete(modelPath);
        }
    }

    [Theory]
    [InlineData(OrtExecutionProviderKind.Cuda)]
    [InlineData(OrtExecutionProviderKind.Cpu)]
    public void GetOptimizedModelPath_KeepsCudaAndCpuOptimizedModelCache(OrtExecutionProviderKind provider)
    {
        var modelPath = CreateTempModelFile();
        try
        {
            var options = new OrtSessionFactoryOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            var path = OrtSessionFactory.GetOptimizedModelPath(modelPath, provider, options);

            Assert.NotNull(path);
            Assert.Contains("ort-cache", path, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(provider.ToString(), path, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".onnx", path, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(modelPath);
        }
    }

    [Theory]
    [InlineData(OrtExecutionProviderKind.TensorRt)]
    [InlineData(OrtExecutionProviderKind.Cuda)]
    [InlineData(OrtExecutionProviderKind.Cpu)]
    public void GetOptimizedModelPath_DisabledGraphOptimizationSkipsAllProviders(OrtExecutionProviderKind provider)
    {
        var modelPath = CreateTempModelFile();
        try
        {
            var options = new OrtSessionFactoryOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_DISABLE_ALL
            };

            var path = OrtSessionFactory.GetOptimizedModelPath(modelPath, provider, options);

            Assert.Null(path);
        }
        finally
        {
            File.Delete(modelPath);
        }
    }

    private static string CreateTempModelFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.onnx");
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
