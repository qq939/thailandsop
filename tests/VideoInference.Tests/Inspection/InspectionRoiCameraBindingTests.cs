using VideoInferenceDemo.ImageInspection;
using VideoInferenceDemo.ImageInspection.Roi;

namespace VideoInferenceDemo.Tests.Inspection;

public sealed class InspectionRoiCameraBindingTests
{
    [Fact]
    public void RoiViewModel_Build_PreservesCameraId()
    {
        var viewModel = new InspectionRoiConfigViewModel(new InspectionRoiConfig
        {
            Id = "roi-1",
            Name = "ROI 1",
            CameraId = "cam-a"
        });

        var built = viewModel.Build();

        Assert.Equal("cam-a", built.CameraId);
    }

    [Fact]
    public void RoiViewModel_UsesPixelTextAndBuildsNormalizedCoordinates()
    {
        var viewModel = new InspectionRoiConfigViewModel(new InspectionRoiConfig
        {
            Id = "roi-1",
            Name = "ROI 1",
            CenterX = 0.25,
            CenterY = 0.5,
            Width = 0.1,
            Height = 0.2
        });

        viewModel.SetCoordinateSpace(1000, 500);

        Assert.Equal("250", viewModel.CenterXText);
        Assert.Equal("250", viewModel.CenterYText);
        Assert.Equal("100", viewModel.WidthText);
        Assert.Equal("100", viewModel.HeightText);

        viewModel.CenterXText = "300";
        viewModel.CenterYText = "125";
        viewModel.WidthText = "200";
        viewModel.HeightText = "50";
        var built = viewModel.Build();

        Assert.Equal(0.3, built.CenterX, precision: 6);
        Assert.Equal(0.25, built.CenterY, precision: 6);
        Assert.Equal(0.2, built.Width, precision: 6);
        Assert.Equal(0.1, built.Height, precision: 6);
    }

    [Fact]
    public void AlignmentViewModel_UsesPixelTextAndBuildsNormalizedCoordinates()
    {
        var viewModel = new InspectionCameraAlignmentConfigViewModel(new InspectionCameraAlignmentConfig
        {
            Enabled = true,
            CenterX = 0.5,
            CenterY = 0.25,
            Width = 0.2,
            Height = 0.1
        });

        viewModel.SetCoordinateSpace(800, 600);

        Assert.Equal("400", viewModel.CenterXText);
        Assert.Equal("150", viewModel.CenterYText);
        Assert.Equal("160", viewModel.WidthText);
        Assert.Equal("60", viewModel.HeightText);

        viewModel.CenterXText = "200";
        viewModel.CenterYText = "300";
        viewModel.WidthText = "80";
        viewModel.HeightText = "120";
        var built = viewModel.Build();

        Assert.Equal(0.25, built.CenterX, precision: 6);
        Assert.Equal(0.5, built.CenterY, precision: 6);
        Assert.Equal(0.1, built.Width, precision: 6);
        Assert.Equal(0.2, built.Height, precision: 6);
    }

    [Fact]
    public void NormalizeForCameraIds_AssignsLegacyRoisToFirstCamera()
    {
        var recipe = new InspectionRecipeEntry
        {
            ReferenceImagePath = @"C:\images\legacy.jpg",
            Rois =
            [
                new InspectionRoiConfig { Id = "roi-empty", Name = "Empty camera", CameraId = string.Empty },
                new InspectionRoiConfig { Id = "roi-unknown", Name = "Unknown camera", CameraId = "missing" },
                new InspectionRoiConfig { Id = "roi-b", Name = "Camera B", CameraId = "cam-b" }
            ]
        };

        var fallback = InspectionRecipeCameraBinding.NormalizeForCameraIds(recipe, ["cam-a", "cam-b"]);

        Assert.Equal("cam-a", fallback);
        Assert.Equal("cam-a", recipe.Rois[0].CameraId);
        Assert.Equal("cam-a", recipe.Rois[1].CameraId);
        Assert.Equal("cam-b", recipe.Rois[2].CameraId);
        Assert.Equal(@"C:\images\legacy.jpg", recipe.ReferenceImagePathsByCameraId["cam-a"]);
    }

    [Fact]
    public void NormalizeForCameraIds_RemovesAlignmentForUnboundCameras()
    {
        var recipe = new InspectionRecipeEntry
        {
            AlignmentByCameraId = new Dictionary<string, InspectionCameraAlignmentConfig>
            {
                ["cam-a"] = new() { Enabled = true, LocatorModelId = "locator-a" },
                ["missing"] = new() { Enabled = true, LocatorModelId = "locator-missing" }
            }
        };

        InspectionRecipeCameraBinding.NormalizeForCameraIds(recipe, ["cam-a", "cam-b"]);

        Assert.True(recipe.AlignmentByCameraId.ContainsKey("cam-a"));
        Assert.False(recipe.AlignmentByCameraId.ContainsKey("missing"));
    }


    [Fact]
    public void RoiWindowViewModel_SavesRoisAndReferenceImagesPerCamera()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "recipes.json");
            var catalog = new InspectionRecipeCatalog
            {
                Recipes =
                [
                    new InspectionRecipeEntry
                    {
                        ProductModel = "A100",
                        TaskId = "appearance",
                        PositionNo = "P01",
                        ReferenceImagePathsByCameraId = new Dictionary<string, string>
                        {
                            ["cam-a"] = @"C:\images\a.jpg",
                            ["cam-b"] = @"C:\images\b.jpg"
                        },
                        Rois =
                        [
                            new InspectionRoiConfig { Id = "roi-a", Name = "ROI A", CameraId = "cam-a", SortOrder = 1 },
                            new InspectionRoiConfig { Id = "roi-b", Name = "ROI B", CameraId = "cam-b", SortOrder = 1 }
                        ]
                    }
                ]
            };
            var cameras = new[]
            {
                new InspectionCameraProfile { Id = "cam-a", Name = "Camera A" },
                new InspectionCameraProfile { Id = "cam-b", Name = "Camera B" }
            };
            var viewModel = new InspectionRoiConfigWindowViewModel(
                path,
                "A100",
                "appearance",
                "P01",
                catalog,
                new InspectionModelSettings(),
                cameras,
                new NullImageFilePicker());

            Assert.Equal("cam-a", Assert.Single(viewModel.Rois).CameraId);
            Assert.Equal(@"C:\images\a.jpg", viewModel.ReferenceImagePath);

            viewModel.SelectedCamera = viewModel.AvailableCameras.Single(camera => camera.Id == "cam-b");
            Assert.Equal("cam-b", Assert.Single(viewModel.Rois).CameraId);
            Assert.Equal(@"C:\images\b.jpg", viewModel.ReferenceImagePath);

            viewModel.ReferenceImagePath = @"C:\images\b-updated.jpg";
            Assert.True(viewModel.TrySave());

            var saved = Assert.Single(catalog.Recipes);
            Assert.Equal(@"C:\images\a.jpg", saved.ReferenceImagePathsByCameraId["cam-a"]);
            Assert.Equal(@"C:\images\b-updated.jpg", saved.ReferenceImagePathsByCameraId["cam-b"]);
            Assert.Contains(saved.Rois, roi => roi.Id == "roi-a" && roi.CameraId == "cam-a");
            Assert.Contains(saved.Rois, roi => roi.Id == "roi-b" && roi.CameraId == "cam-b");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RoiWindowViewModel_UsesOnlyCurrentTaskRecipeContext()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "recipes.json");
            var catalog = new InspectionRecipeCatalog
            {
                Recipes =
                [
                    new InspectionRecipeEntry
                    {
                        ProductModel = "A100",
                        TaskId = "appearance",
                        PositionNo = "P01"
                    },
                    new InspectionRecipeEntry
                    {
                        ProductModel = "A100",
                        TaskId = "appearance",
                        PositionNo = "P02"
                    }
                ]
            };
            var cameras = new[]
            {
                new InspectionCameraProfile { Id = "cam-a", Name = "Camera A" }
            };

            var viewModel = new InspectionRoiConfigWindowViewModel(
                path,
                "A100",
                "appearance",
                "P01",
                catalog,
                new InspectionModelSettings(),
                cameras,
                new NullImageFilePicker());

            var context = Assert.Single(viewModel.Contexts);
            Assert.Equal("A100", context.ProductModel);
            Assert.Equal("appearance", context.TaskId);
            Assert.Equal("P01", context.PositionNo);
            Assert.False(viewModel.CanSwitchContext);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RoiWindowViewModel_AddRoi_BindsNewRoisToSelectedCamera()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "recipes.json");
            var catalog = new InspectionRecipeCatalog
            {
                Recipes =
                [
                    new InspectionRecipeEntry
                    {
                        ProductModel = "A100",
                        TaskId = "appearance",
                        PositionNo = "P01",
                        Rois = []
                    }
                ]
            };
            var cameras = new[]
            {
                new InspectionCameraProfile { Id = "cam-a", Name = "Camera A" },
                new InspectionCameraProfile { Id = "cam-b", Name = "Camera B" }
            };
            var viewModel = new InspectionRoiConfigWindowViewModel(
                path,
                "A100",
                "appearance",
                "P01",
                catalog,
                new InspectionModelSettings(),
                cameras,
                new NullImageFilePicker());

            viewModel.SelectedCamera = viewModel.AvailableCameras.Single(camera => camera.Id == "cam-b");
            viewModel.AddRoiCommand.Execute(null);
            viewModel.AddRoiCommand.Execute(null);

            Assert.Equal(["ROI-2", "ROI-3"], viewModel.Rois.Select(roi => roi.Name).ToArray());
            Assert.All(viewModel.Rois, roi => Assert.Equal("cam-b", roi.CameraId));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class NullImageFilePicker : VideoInferenceDemo.ImageInspection.Services.IImageFilePicker
    {
        public string? PickImageFile() => null;
    }
}
