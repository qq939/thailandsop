using Xunit;
using VideoInferenceDemo.ImageInspection;

namespace VideoInferenceDemo.Tests.Inspection;

public sealed class InspectionTaskTriggerSettingsTests
{
    [Fact]
    public void CameraOpenOptions_Normalize_PreservesHardwareLine0()
    {
        var options = new CameraOpenOptions(
            CameraProviderIds.HikRobot,
            CameraIndex: 0,
            DeviceId: "camera-1",
            TargetFps: 10,
            TriggerMode: CameraTriggerMode.HardwareLine0);

        var normalized = options.Normalize();

        Assert.Equal(CameraTriggerMode.HardwareLine0, normalized.TriggerMode);
    }

    [Fact]
    public void InspectionTaskInstance_Normalize_PreservesCameraCallbackTriggerMode()
    {
        var definition = new InspectionTaskDefinition
        {
            Id = "appearance-check",
            Name = "Appearance",
            ActionType = InspectionActionTypes.RoiInspection
        }.Normalize(1);
        var instance = new InspectionTaskInstance
        {
            Id = "station-1-task",
            Name = "Station 1",
            DefinitionId = definition.Id,
            StationId = "station-1",
            TriggerMode = InspectionTaskTriggerMode.CameraCallback,
            CameraIds = ["camera-1"]
        };

        var normalized = instance.Normalize(1, [definition]);

        Assert.Equal(InspectionTaskTriggerMode.CameraCallback, normalized.TriggerMode);
    }

    [Fact]
    public void InspectionTaskSettingsStorage_RoundTripsTriggerMode()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "inspection_task_config.json");
            var camera = InspectionCameraProfile.CreateDefault(1);
            var definition = new InspectionTaskDefinition
            {
                Id = "appearance-check",
                Name = "Appearance",
                ActionType = InspectionActionTypes.RoiInspection
            };
            var instance = new InspectionTaskInstance
            {
                Id = "station-1-task",
                Name = "Station 1",
                DefinitionId = definition.Id,
                StationId = "station-1",
                TriggerMode = InspectionTaskTriggerMode.CameraCallback,
                CameraIds = [camera.Id]
            };

            InspectionTaskSettingsStorage.Save(
                path,
                new InspectionTaskSettings
                {
                    Definitions = [definition],
                    Instances = [instance]
                });

            var loaded = InspectionTaskSettingsStorage.Load(path, [camera], seedRecipe: null);

            Assert.Equal(InspectionTaskTriggerMode.CameraCallback, Assert.Single(loaded.Instances).TriggerMode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InspectionTaskSettingsStorage_LoadsMissingTriggerModeAsCommand()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "inspection_task_config.json");
            File.WriteAllText(
                path,
                """
                {
                  "Definitions": [
                    {
                      "Id": "appearance-check",
                      "Name": "Appearance",
                      "ActionType": "roi-inspection",
                      "Enabled": true
                    }
                  ],
                  "Instances": [
                    {
                      "Id": "station-1-task",
                      "Name": "Station 1",
                      "DefinitionId": "appearance-check",
                      "StationId": "station-1",
                      "Enabled": true,
                      "CameraIds": []
                    }
                  ]
                }
                """);

            var loaded = InspectionTaskSettingsStorage.Load(path);

            Assert.Equal(InspectionTaskTriggerMode.TriggerCommand, Assert.Single(loaded.Instances).TriggerMode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InspectionTaskSettingsStorage_AdjustsCommandTaskToCallbackForHardwareCameras()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "inspection_task_config.json");
            var camera = CreateCamera("cam-hw", CameraProviderIds.HikRobot, CameraTriggerMode.HardwareLine0);
            InspectionTaskSettingsStorage.Save(path, CreateSettings(InspectionTaskTriggerMode.TriggerCommand, camera.Id));

            var loaded = InspectionTaskSettingsStorage.Load(path, [camera], seedRecipe: null, out var adjusted);

            Assert.True(adjusted);
            Assert.Equal(InspectionTaskTriggerMode.CameraCallback, Assert.Single(loaded.Instances).TriggerMode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InspectionTaskSettingsStorage_KeepsCommandTaskForSoftwareCameras()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "inspection_task_config.json");
            var camera = CreateCamera("cam-soft", CameraProviderIds.HikRobot, CameraTriggerMode.Software);
            InspectionTaskSettingsStorage.Save(path, CreateSettings(InspectionTaskTriggerMode.TriggerCommand, camera.Id));

            var loaded = InspectionTaskSettingsStorage.Load(path, [camera], seedRecipe: null, out var adjusted);

            Assert.False(adjusted);
            Assert.Equal(InspectionTaskTriggerMode.TriggerCommand, Assert.Single(loaded.Instances).TriggerMode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void InspectionTaskSettings_CreateDefault_UsesCallbackForHardwareCameras()
    {
        var camera = CreateCamera("cam-hw", CameraProviderIds.HikRobot, CameraTriggerMode.HardwareLine0);

        var settings = InspectionTaskSettings.CreateDefault([camera]);

        Assert.Equal(InspectionTaskTriggerMode.CameraCallback, Assert.Single(settings.Instances).TriggerMode);
    }

    [Fact]
    public void InspectionTaskTriggerCompatibility_ReportsMixedCameraTriggerModes()
    {
        var hardware = CreateCamera("cam-hw", CameraProviderIds.HikRobot, CameraTriggerMode.HardwareLine0);
        var software = CreateCamera("cam-soft", CameraProviderIds.HikRobot, CameraTriggerMode.Software);

        var compatibility = InspectionTaskTriggerCompatibility.ResolveCompatibility([hardware.Id, software.Id], [hardware, software]);

        Assert.Equal(InspectionTaskCameraTriggerCompatibility.Mixed, compatibility);
    }

    private static InspectionTaskSettings CreateSettings(InspectionTaskTriggerMode triggerMode, params string[] cameraIds)
    {
        var definition = new InspectionTaskDefinition
        {
            Id = "appearance-check",
            Name = "Appearance",
            ActionType = InspectionActionTypes.RoiInspection
        };
        var instance = new InspectionTaskInstance
        {
            Id = "station-1-task",
            Name = "Station 1",
            DefinitionId = definition.Id,
            StationId = "station-1",
            TriggerMode = triggerMode,
            CameraIds = cameraIds.ToList()
        };

        return new InspectionTaskSettings
        {
            Definitions = [definition],
            Instances = [instance]
        };
    }

    private static InspectionCameraProfile CreateCamera(string id, string providerId, CameraTriggerMode triggerMode)
    {
        return new InspectionCameraProfile
        {
            Id = id,
            Name = id,
            ProviderId = providerId,
            TriggerMode = triggerMode
        }.Normalize(1);
    }
}
