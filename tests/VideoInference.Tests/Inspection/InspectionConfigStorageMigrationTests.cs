using System.Text.Json;
using VideoInferenceDemo.ImageInspection;

namespace VideoInferenceDemo.Tests.Inspection;

[Collection("DbSession")]
public sealed class InspectionConfigStorageMigrationTests : IDisposable
{
    private readonly string _root;

    public InspectionConfigStorageMigrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "inspection-config-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        DbSession.Reset();
    }

    [Fact]
    public void CameraSettings_Load_MigratesLegacyJsonIntoConfigDatabase()
    {
        var legacyPath = Path.Combine(_root, "inspection_camera_config.json");
        File.WriteAllText(
            legacyPath,
            JsonSerializer.Serialize(new InspectionCameraSettings
            {
                SelectedCameraId = "cam-a",
                Cameras =
                [
                    new InspectionCameraProfile
                    {
                        Id = "cam-a",
                        Name = "Line Camera",
                        ProviderId = CameraProviderIds.HikRobot,
                        TriggerMode = CameraTriggerMode.HardwareLine0
                    }
                ]
            }));
        DbSession.InitializeSplit(Path.Combine(_root, "workspace_config.db"), Path.Combine(_root, "results"));

        var loaded = InspectionCameraSettingsStorage.Load(legacyPath);

        Assert.Equal("cam-a", loaded.SelectedCameraId);
        Assert.Equal(CameraTriggerMode.HardwareLine0, Assert.Single(loaded.Cameras).TriggerMode);
        var state = LoadState("image_inspection_camera_settings");
        Assert.NotNull(state);
        Assert.Contains("Line Camera", state!.Value);
    }

    [Fact]
    public void CameraSettings_Load_RewritesDuplicateCameraIds()
    {
        var path = Path.Combine(_root, "inspection_camera_config.json");
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(new InspectionCameraSettings
            {
                SelectedCameraId = "cam-a",
                Cameras =
                [
                    new InspectionCameraProfile
                    {
                        Id = "cam-a",
                        Name = "Up",
                        ProviderId = CameraProviderIds.HikRobot
                    },
                    new InspectionCameraProfile
                    {
                        Id = "cam-a",
                        Name = "Down",
                        ProviderId = CameraProviderIds.HikRobot
                    }
                ]
            }));

        var loaded = InspectionCameraSettingsStorage.Load(path);

        Assert.Equal(2, loaded.Cameras.Count);
        Assert.Equal(2, loaded.Cameras.Select(camera => camera.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("cam-a", loaded.Cameras[0].Id);
        Assert.Equal("cam-a-2", loaded.Cameras[1].Id);
        Assert.Equal("cam-a", loaded.SelectedCameraId);
    }

    [Fact]
    public void TaskSettings_Load_MigratesLegacyJsonAndNormalizesTriggerMode()
    {
        var legacyPath = Path.Combine(_root, "inspection_task_config.json");
        var camera = new InspectionCameraProfile
        {
            Id = "cam-hw",
            Name = "Hardware Camera",
            ProviderId = CameraProviderIds.HikRobot,
            TriggerMode = CameraTriggerMode.HardwareLine0
        }.Normalize(1);
        File.WriteAllText(legacyPath, JsonSerializer.Serialize(CreateTaskSettings(InspectionTaskTriggerMode.TriggerCommand, camera.Id)));
        DbSession.InitializeSplit(Path.Combine(_root, "workspace_config.db"), Path.Combine(_root, "results"));

        var loaded = InspectionTaskSettingsStorage.Load(legacyPath, [camera], seedRecipe: null, out var adjusted);

        Assert.True(adjusted);
        Assert.Equal(InspectionTaskTriggerMode.CameraCallback, Assert.Single(loaded.Instances).TriggerMode);
        var state = LoadState("image_inspection_task_settings");
        Assert.NotNull(state);
        Assert.Contains("station-1-task", state!.Value);
    }

    [Fact]
    public void RecipeCatalog_Load_MigratesLegacyJsonAndPreservesCameraScopedData()
    {
        var legacyPath = Path.Combine(_root, "inspection_recipe_config.json");
        File.WriteAllText(
            legacyPath,
            JsonSerializer.Serialize(new InspectionRecipeCatalog
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
                            ["cam-a"] = @"C:\images\a.jpg"
                        },
                        AlignmentByCameraId = new Dictionary<string, InspectionCameraAlignmentConfig>
                        {
                            ["cam-a"] = new()
                            {
                                Enabled = true,
                                LocatorModelId = "locator",
                                LocatorClassName = "datum",
                                CenterX = 0.4,
                                CenterY = 0.5,
                                Width = 0.2,
                                Height = 0.1,
                                AngleDeg = 8
                            }
                        },
                        Rois =
                        [
                            new InspectionRoiConfig
                            {
                                Id = "roi-a",
                                Name = "ROI A",
                                CameraId = "cam-a"
                            }
                        ]
                    }
                ]
            }));
        DbSession.InitializeSplit(Path.Combine(_root, "workspace_config.db"), Path.Combine(_root, "results"));

        var loaded = InspectionRecipeCatalogStorage.Load(legacyPath);

        var recipe = Assert.Single(loaded.Recipes);
        Assert.Equal(@"C:\images\a.jpg", recipe.ReferenceImagePathsByCameraId["cam-a"]);
        Assert.Equal("locator", recipe.AlignmentByCameraId["cam-a"].LocatorModelId);
        Assert.Equal("cam-a", Assert.Single(recipe.Rois).CameraId);
        var state = LoadState("image_inspection_recipe_catalog");
        Assert.NotNull(state);
        Assert.Contains("locator", state!.Value);
    }

    [Fact]
    public void Save_WhenDbInitialized_WritesConfigDatabaseOnly()
    {
        var cameraPath = Path.Combine(_root, "inspection_camera_config.json");
        var taskPath = Path.Combine(_root, "inspection_task_config.json");
        var recipePath = Path.Combine(_root, "inspection_recipe_config.json");
        File.WriteAllText(cameraPath, "legacy-camera");
        File.WriteAllText(taskPath, "legacy-task");
        File.WriteAllText(recipePath, "legacy-recipe");
        DbSession.InitializeSplit(Path.Combine(_root, "workspace_config.db"), Path.Combine(_root, "results"));

        InspectionCameraSettingsStorage.Save(
            cameraPath,
            new InspectionCameraSettings
            {
                SelectedCameraId = "cam-db",
                Cameras =
                [
                    new InspectionCameraProfile
                    {
                        Id = "cam-db",
                        Name = "DB Camera"
                    }
                ]
            });
        InspectionTaskSettingsStorage.Save(taskPath, CreateTaskSettings(InspectionTaskTriggerMode.TriggerCommand, "cam-db"));
        InspectionRecipeCatalogStorage.Save(
            recipePath,
            new InspectionRecipeCatalog
            {
                Recipes =
                [
                    new InspectionRecipeEntry
                    {
                        ProductModel = "DB Model",
                        TaskId = "appearance",
                        PositionNo = "P01"
                    }
                ]
            });

        Assert.Equal("legacy-camera", File.ReadAllText(cameraPath));
        Assert.Equal("legacy-task", File.ReadAllText(taskPath));
        Assert.Equal("legacy-recipe", File.ReadAllText(recipePath));
        Assert.Equal("cam-db", InspectionCameraSettingsStorage.Load(cameraPath).SelectedCameraId);
        Assert.Equal("station-1-task", Assert.Single(InspectionTaskSettingsStorage.Load(taskPath).Instances).Id);
        Assert.Equal("DB Model", Assert.Single(InspectionRecipeCatalogStorage.Load(recipePath).Recipes).ProductModel);
    }

    public void Dispose()
    {
        DbSession.Reset();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private static CameraSettingsStateEntity? LoadState(string key)
    {
        return DbSession.ConfigDb.Queryable<CameraSettingsStateEntity>()
            .Where(entity => entity.Key == key)
            .First();
    }

    private static InspectionTaskSettings CreateTaskSettings(InspectionTaskTriggerMode triggerMode, params string[] cameraIds)
    {
        return new InspectionTaskSettings
        {
            Definitions =
            [
                new InspectionTaskDefinition
                {
                    Id = "appearance-check",
                    Name = "Appearance",
                    ActionType = InspectionActionTypes.RoiInspection
                }
            ],
            Instances =
            [
                new InspectionTaskInstance
                {
                    Id = "station-1-task",
                    Name = "Station 1",
                    DefinitionId = "appearance-check",
                    StationId = "station-1",
                    TriggerMode = triggerMode,
                    CameraIds = cameraIds.ToList()
                }
            ]
        };
    }
}
