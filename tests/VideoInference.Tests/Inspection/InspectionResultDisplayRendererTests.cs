using OpenCvSharp;
using VideoInferenceDemo.ImageInspection.Runtime;

namespace VideoInferenceDemo.Tests.Inspection;

public sealed class InspectionResultDisplayRendererTests
{
    [Fact]
    public void DrawSegmentationOverlays_WarpsRoiMaskBackToImage()
    {
        using var image = new Mat(10, 10, MatType.CV_8UC3, Scalar.All(0));
        var result = new InspectionCycleResult
        {
            RecipeKey = new InspectionRecipeKey("A100", "appearance", "P01"),
            Decision = InspectionCycleDecision.Ng,
            ResolvedRois =
            [
                new RoiDefinition
                {
                    Id = "roi-1",
                    Name = "ROI 1",
                    CenterX = 0.5,
                    CenterY = 0.5,
                    Width = 0.4,
                    Height = 0.4
                }
            ],
            RoiResults =
            [
                new InspectionRoiResult
                {
                    RoiId = "roi-1",
                    RoiName = "ROI 1",
                    Decision = InspectionCycleDecision.Ng,
                    SegmentationMask = [255, 255, 255, 255],
                    SegmentationMaskWidth = 2,
                    SegmentationMaskHeight = 2
                }
            ]
        };

        InspectionResultDisplayRenderer.DrawSegmentationOverlays(image, result);

        Assert.True(CountChangedPixels(image) > 0);
        Assert.True(IsChanged(image.At<Vec3b>(5, 5)));
    }

    private static int CountChangedPixels(Mat image)
    {
        var count = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (IsChanged(image.At<Vec3b>(y, x)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool IsChanged(Vec3b pixel)
    {
        return pixel.Item0 != 0 || pixel.Item1 != 0 || pixel.Item2 != 0;
    }
}
