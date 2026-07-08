using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace VideoInferenceDemo.Tests.Inference;

public sealed class UnetSegmentationTests
{
    [Fact]
    public void PresencePreprocess_ConvertsBgrToRgbAndAppliesImageNetNormalization()
    {
        using var image = new Mat(1, 1, MatType.CV_8UC3, new Scalar(10, 20, 30));
        var preprocessor = new PresenceClassificationPreprocessor("input", 1, 1);

        var input = preprocessor.Preprocess(image, out var context);

        Assert.Equal(1, context.OriginalWidth);
        Assert.Equal(1, context.OriginalHeight);
        var named = Assert.Single(input.Inputs);
        Assert.Equal("input", named.Name);
        var tensor = Assert.IsAssignableFrom<Tensor<float>>(named.Value);
        Assert.Equal([1, 3, 1, 1], tensor.Dimensions.ToArray());
        Assert.Equal(Normalize(30, 0.485f, 0.229f), tensor[0, 0, 0, 0], precision: 5);
        Assert.Equal(Normalize(20, 0.456f, 0.224f), tensor[0, 1, 0, 0], precision: 5);
        Assert.Equal(Normalize(10, 0.406f, 0.225f), tensor[0, 2, 0, 0], precision: 5);
    }

    [Fact]
    public void PresencePostprocess_UsesSoftmaxAndAbsentThreshold()
    {
        var logits = new DenseTensor<float>(new[] { 1, 2 });
        logits[0, 0] = 0f;
        logits[0, 1] = 1f;
        var postprocessor = new PresenceClassificationPostprocessor(
            "logits",
            new PresenceClassificationMetadata
            {
                PresentClass = "OK",
                AbsentClass = "NG",
                ProbabilityThreshold = 0.5f
            },
            ["OK", "NG"]);

        var result = postprocessor.Postprocess(logits);

        Assert.True(result.IsAbsent);
        Assert.Equal("NG", result.Absent?.ClassName);
        Assert.Equal(0.731f, result.AbsentProbability, precision: 3);
        Assert.Contains("无产品", result.SummaryText);
    }

    [Fact]
    public void Preprocess_ConvertsBgrToRgbAndAppliesImageNetNormalization()
    {
        using var image = new Mat(1, 1, MatType.CV_8UC3, new Scalar(10, 20, 30));
        var preprocessor = new UnetSegmentationPreprocessor("input", 1, 1);

        var input = preprocessor.Preprocess(image, out var context);

        Assert.Equal(1, context.OriginalWidth);
        Assert.Equal(1, context.OriginalHeight);
        var named = Assert.Single(input.Inputs);
        Assert.Equal("input", named.Name);
        var tensor = Assert.IsAssignableFrom<Tensor<float>>(named.Value);
        Assert.Equal([1, 3, 1, 1], tensor.Dimensions.ToArray());
        Assert.Equal(Normalize(30, 0.485f, 0.229f), tensor[0, 0, 0, 0], precision: 5);
        Assert.Equal(Normalize(20, 0.456f, 0.224f), tensor[0, 1, 0, 0], precision: 5);
        Assert.Equal(Normalize(10, 0.406f, 0.225f), tensor[0, 2, 0, 0], precision: 5);
    }

    [Fact]
    public void Postprocess_FiltersConnectedComponentsAndBuildsDefectSummary()
    {
        var logits = new DenseTensor<float>(new[] { 1, 1, 8, 8 });
        Fill(logits, x: 1, y: 1, width: 4, height: 3, value: 4f);
        logits[0, 0, 7, 7] = 4f;
        var postprocessor = new UnetSegmentationPostprocessor(
            "logits",
            new UnetSegmentationMetadata
            {
                ProbabilityThreshold = 0.6f,
                MinComponentArea = 6
            });

        var result = postprocessor.Postprocess(logits, new UnetImageTransformContext(8, 8));

        Assert.True(result.HasDefect);
        Assert.Equal(2, result.RawComponentCount);
        var component = Assert.Single(result.Components);
        Assert.Equal(1, component.Index);
        Assert.Equal(1, component.X);
        Assert.Equal(1, component.Y);
        Assert.Equal(4, component.Width);
        Assert.Equal(3, component.Height);
        Assert.Equal(12, component.AreaPx);
        Assert.True(component.PerimeterPx > 0f);
        Assert.Equal(component.AreaPx / component.PerimeterPx, component.AreaPerimeterRatio, precision: 5);
        Assert.Equal(12, result.Mask.Count(value => value == 255));
        Assert.Contains("accepted=1", result.SummaryText);
        Assert.Contains("raw=2", result.SummaryText);
        Assert.Contains("decision=NG", result.SummaryText);
        Assert.Contains("bbox=(1,1,4,3)", result.ComponentsText);
    }

    private static void Fill(DenseTensor<float> tensor, int x, int y, int width, int height, float value)
    {
        for (var row = y; row < y + height; row++)
        {
            for (var col = x; col < x + width; col++)
            {
                tensor[0, 0, row, col] = value;
            }
        }
    }

    private static float Normalize(int channelValue, float mean, float std)
    {
        return ((channelValue / 255f) - mean) / std;
    }
}
