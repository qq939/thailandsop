using OpenCvSharp;
using VideoInferenceDemo.ImageInspection.Runtime;

namespace VideoInferenceDemo.Tests.Inspection;

public sealed class InspectionActionTests
{
    [Fact]
    public void Execute_ResolvesRecipeAndSortsRoisAndModels()
    {
        using var image = new Mat(24, 24, MatType.CV_8UC3, Scalar.All(0));
        var recipe = new InspectionRecipe
        {
            Key = new InspectionRecipeKey("A100", "appearance", "P01"),
            Calibration = new CalibrationContext
            {
                CalibrationId = "cal-a100-p01",
                PixelsPerUnitX = 10.5,
                PixelsPerUnitY = 10.5
            },
            Rois =
            [
                new RoiDefinition { Id = "roi-2", Name = "ROI 2", SortOrder = 2, Enabled = true },
                new RoiDefinition { Id = "roi-1", Name = "ROI 1", SortOrder = 1, Enabled = true },
                new RoiDefinition { Id = "roi-3", Name = "ROI 3", SortOrder = 3, Enabled = false }
            ],
            ModelBindings =
            [
                new InspectionModelReference { ModelId = "model-b", Sequence = 2 },
                new InspectionModelReference { ModelId = "model-a", Sequence = 1 }
            ]
        };

        var action = new InspectionAction(
            new StubRecipeProvider(recipe),
            new PassThroughModelReferenceResolver());

        var result = action.Execute(new InspectionRequest
        {
            OriginalImage = image,
            ProductModel = "A100",
            TaskId = "appearance",
            PositionNo = "P01",
            StationId = "station-01",
            TriggerId = "trigger-01"
        });

        Assert.Equal("A100", result.RecipeKey.ProductModel);
        Assert.Equal("appearance", result.RecipeKey.TaskId);
        Assert.Equal("P01", result.RecipeKey.PositionNo);
        Assert.Equal("station-01", result.StationId);
        Assert.Equal("trigger-01", result.TriggerId);
        Assert.Equal("cal-a100-p01", result.Calibration.CalibrationId);
        Assert.Equal(["roi-1", "roi-2"], result.ResolvedRois.Select(roi => roi.Id).ToArray());
        Assert.Equal(["model-a", "model-b"], result.ResolvedModels.Select(model => model.ModelId).ToArray());
        Assert.Equal(InspectionCycleDecision.Unknown, result.Decision);
    }

    [Fact]
    public void Execute_FiltersRoisByCameraId()
    {
        using var image = new Mat(24, 24, MatType.CV_8UC3, Scalar.All(0));
        var recipe = new InspectionRecipe
        {
            Key = new InspectionRecipeKey("A100", "appearance", "P01"),
            Rois =
            [
                new RoiDefinition { Id = "roi-a", Name = "ROI A", CameraId = "cam-a", SortOrder = 1, Enabled = true },
                new RoiDefinition { Id = "roi-b", Name = "ROI B", CameraId = "cam-b", SortOrder = 2, Enabled = true }
            ]
        };

        var action = new InspectionAction(
            new StubRecipeProvider(recipe),
            new PassThroughModelReferenceResolver());

        var result = action.Execute(new InspectionRequest
        {
            OriginalImage = image,
            ProductModel = "A100",
            TaskId = "appearance",
            PositionNo = "P01",
            CameraId = "cam-b"
        });

        Assert.Equal(["roi-b"], result.ResolvedRois.Select(roi => roi.Id).ToArray());
    }

    [Fact]
    public void RoiInferenceExecute_GlobalAlignment_TransformsRoisBeforeInference()
    {
        using var image = new Mat(100, 100, MatType.CV_8UC3, Scalar.All(0));
        var recipe = new InspectionRecipe
        {
            Key = new InspectionRecipeKey("A100", "appearance", "P01"),
            Rois =
            [
                new RoiDefinition
                {
                    Id = "roi-a",
                    Name = "ROI A",
                    CameraId = "cam-a",
                    CenterX = 0.6,
                    CenterY = 0.5,
                    Width = 0.1,
                    Height = 0.1,
                    AngleDeg = 10,
                    ModelId = "roi-model",
                    SortOrder = 1,
                    Enabled = true
                }
            ],
            AlignmentByCameraId = new Dictionary<string, CameraAlignmentDefinition>
            {
                ["cam-a"] = new()
                {
                    Enabled = true,
                    LocatorModelId = "locator",
                    LocatorClassId = 1,
                    CenterX = 0.5,
                    CenterY = 0.5,
                    AngleDeg = 0
                }
            }
        };
        var runtime = new FakeInspectionModelRuntime();
        runtime.Results["locator"] = new InspectionModelExecutionResult(
            "locator",
            "Locator",
            VisionTaskKind.ObbDetection,
            new ObbDetectionPayload(
            [
                new YoloObbDetection(1, "datum", 0.95f, 60, 50, 20, 10, 90)
            ]),
            "fake",
            new Dictionary<string, string> { ["locator.minScore"] = "0.7" });
        runtime.Results["roi-model"] = new InspectionModelExecutionResult(
            "roi-model",
            "ROI model",
            VisionTaskKind.Detection,
            new DetectionPayload([]),
            "fake",
            new Dictionary<string, string>());

        var action = new RoiInferenceInspectionAction(
            new StubRecipeProvider(recipe),
            new PassThroughModelReferenceResolver(),
            runtime);

        var result = action.Execute(new InspectionRequest
        {
            OriginalImage = image,
            ProductModel = "A100",
            TaskId = "appearance",
            PositionNo = "P01",
            CameraId = "cam-a"
        });

        var aligned = Assert.Single(result.ResolvedRois);
        Assert.Equal(InspectionCycleDecision.Ok, result.Decision);
        Assert.Equal(0.6, aligned.CenterX, 3);
        Assert.Equal(0.6, aligned.CenterY, 3);
        Assert.Equal(100, aligned.AngleDeg, 3);
        Assert.Equal(1, runtime.ExecuteCounts["locator"]);
        Assert.Equal(1, runtime.ExecuteCounts["roi-model"]);
        Assert.Equal("10", result.Metadata["alignment.dxPx"]);
        Assert.Equal("0", result.Metadata["alignment.dyPx"]);
        Assert.Equal("90", result.Metadata["alignment.angleDeg"]);
    }

    [Fact]
    public void RoiInferenceExecute_GlobalAlignment_NormalizesObbSwappedWidthHeightAngle()
    {
        using var image = new Mat(100, 100, MatType.CV_8UC3, Scalar.All(0));
        var recipe = new InspectionRecipe
        {
            Key = new InspectionRecipeKey("A100", "appearance", "P01"),
            Rois =
            [
                new RoiDefinition
                {
                    Id = "roi-a",
                    Name = "ROI A",
                    CameraId = "cam-a",
                    CenterX = 0.6,
                    CenterY = 0.5,
                    Width = 0.2,
                    Height = 0.05,
                    AngleDeg = 0,
                    ModelId = "roi-model",
                    SortOrder = 1,
                    Enabled = true
                }
            ],
            AlignmentByCameraId = new Dictionary<string, CameraAlignmentDefinition>
            {
                ["cam-a"] = new()
                {
                    Enabled = true,
                    LocatorModelId = "locator",
                    LocatorClassId = 1,
                    CenterX = 0.5,
                    CenterY = 0.5,
                    Width = 0.4,
                    Height = 0.1,
                    AngleDeg = 0
                }
            }
        };
        var runtime = new FakeInspectionModelRuntime();
        runtime.Results["locator"] = new InspectionModelExecutionResult(
            "locator",
            "Locator",
            VisionTaskKind.ObbDetection,
            new ObbDetectionPayload(
            [
                new YoloObbDetection(1, "datum", 0.95f, 60, 50, 10, 40, 90)
            ]),
            "fake",
            new Dictionary<string, string> { ["locator.minScore"] = "0.7" });
        runtime.Results["roi-model"] = new InspectionModelExecutionResult(
            "roi-model",
            "ROI model",
            VisionTaskKind.Detection,
            new DetectionPayload([]),
            "fake",
            new Dictionary<string, string>());

        var action = new RoiInferenceInspectionAction(
            new StubRecipeProvider(recipe),
            new PassThroughModelReferenceResolver(),
            runtime);

        var result = action.Execute(new InspectionRequest
        {
            OriginalImage = image,
            ProductModel = "A100",
            TaskId = "appearance",
            PositionNo = "P01",
            CameraId = "cam-a"
        });

        var aligned = Assert.Single(result.ResolvedRois);
        Assert.Equal(0.7, aligned.CenterX, 3);
        Assert.Equal(0.5, aligned.CenterY, 3);
        Assert.Equal(0, aligned.AngleDeg, 3);
        Assert.Equal("90", result.Metadata["alignment.detectedRawAngleDeg"]);
        Assert.Equal("0", result.Metadata["alignment.detectedResolvedAngleDeg"]);
        Assert.Equal("40", result.Metadata["alignment.detectedResolvedWidth"]);
        Assert.Equal("10", result.Metadata["alignment.detectedResolvedHeight"]);
        Assert.Equal("0", result.Metadata["alignment.angleDeg"]);
    }

    [Fact]
    public void RoiInferenceExecute_GlobalAlignmentFailure_DoesNotRunRoiModel()
    {
        using var image = new Mat(100, 100, MatType.CV_8UC3, Scalar.All(0));
        var recipe = new InspectionRecipe
        {
            Key = new InspectionRecipeKey("A100", "appearance", "P01"),
            Rois =
            [
                new RoiDefinition
                {
                    Id = "roi-a",
                    Name = "ROI A",
                    CameraId = "cam-a",
                    ModelId = "roi-model",
                    SortOrder = 1,
                    Enabled = true
                }
            ],
            AlignmentByCameraId = new Dictionary<string, CameraAlignmentDefinition>
            {
                ["cam-a"] = new()
                {
                    Enabled = true,
                    LocatorModelId = "locator",
                    LocatorClassId = 1,
                    CenterX = 0.5,
                    CenterY = 0.5
                }
            }
        };
        var runtime = new FakeInspectionModelRuntime();
        runtime.Results["locator"] = new InspectionModelExecutionResult(
            "locator",
            "Locator",
            VisionTaskKind.ObbDetection,
            new ObbDetectionPayload(
            [
                new YoloObbDetection(1, "datum", 0.4f, 60, 50, 20, 10, 0)
            ]),
            "fake",
            new Dictionary<string, string> { ["locator.minScore"] = "0.7" });

        var action = new RoiInferenceInspectionAction(
            new StubRecipeProvider(recipe),
            new PassThroughModelReferenceResolver(),
            runtime);

        var result = action.Execute(new InspectionRequest
        {
            OriginalImage = image,
            ProductModel = "A100",
            TaskId = "appearance",
            PositionNo = "P01",
            CameraId = "cam-a"
        });

        var roiResult = Assert.Single(result.RoiResults);
        var finding = Assert.Single(roiResult.Findings);
        Assert.Equal(InspectionCycleDecision.Ng, result.Decision);
        Assert.Equal("global-alignment-failed", finding.Code);
        Assert.Equal(1, runtime.ExecuteCounts["locator"]);
        Assert.False(runtime.ExecuteCounts.ContainsKey("roi-model"));
    }

    private sealed class StubRecipeProvider : IInspectionRecipeProvider
    {
        private readonly InspectionRecipe _recipe;

        public StubRecipeProvider(InspectionRecipe recipe)
        {
            _recipe = recipe;
        }

        public InspectionRecipe Get(InspectionRecipeKey key)
        {
            Assert.Equal(_recipe.Key, key);
            return _recipe;
        }
    }

    private sealed class PassThroughModelReferenceResolver : IModelReferenceResolver
    {
        public IReadOnlyList<InspectionModelReference> Resolve(InspectionRecipe recipe)
        {
            return recipe.ModelBindings;
        }
    }

    private sealed class FakeInspectionModelRuntime : IInspectionModelRuntime
    {
        public Dictionary<string, InspectionModelExecutionResult> Results { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, int> ExecuteCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

        public InspectionModelExecutionResult Execute(string modelId, Mat image)
        {
            ExecuteCounts.TryGetValue(modelId, out var count);
            ExecuteCounts[modelId] = count + 1;
            return Results[modelId];
        }

        public void Warmup(string modelId, int width = 640, int height = 640, int iterations = 5)
        {
        }
    }
}
