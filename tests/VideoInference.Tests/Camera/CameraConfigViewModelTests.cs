using System.Collections.ObjectModel;
using Xunit;

namespace VideoInferenceDemo.Tests.Camera;

[Collection("DbSession")]
public sealed class CameraConfigViewModelTests
{
    [Fact]
    public void TrySave_ExposesSelectedCameraPrimaryTask()
    {
        using var context = new DesktopCoordinatorTestContext();
        var configPath = Path.Combine(context.RootDirectory, "camera_config.json");
        var taskA = CreateTask("detector-a", "Detector A");
        var taskB = CreateTask("detector-b", "Detector B");
        var vm = CreateViewModel(
            configPath,
            new CameraSettings
            {
                Cameras = new List<CameraProfile> { CameraProfile.CreateDefault(1) }
            },
            new[] { taskA, taskB },
            taskA);

        Assert.NotNull(vm.SelectedCamera);
        vm.SelectedCamera!.PrimaryTaskId = taskB.Id;

        Assert.True(vm.TrySave(), vm.ErrorMessage);
        Assert.Same(taskB, vm.SavedPrimaryTask);

        var saved = CameraSettingsStorage.Load(configPath);
        Assert.Equal(taskB.Id, saved.Cameras.Single().PrimaryTaskId);
    }

    [Fact]
    public void TrySave_PreservesDifferentPrimaryTasksPerCamera()
    {
        using var context = new DesktopCoordinatorTestContext();
        var configPath = Path.Combine(context.RootDirectory, "camera_config.json");
        var taskA = CreateTask("detector-a", "Detector A");
        var taskB = CreateTask("detector-b", "Detector B");
        var first = CameraProfile.CreateDefault(1);
        first.Name = "Camera A";
        var second = CameraProfile.CreateDefault(2);
        second.Name = "Camera B";
        var vm = CreateViewModel(
            configPath,
            new CameraSettings
            {
                Cameras = new List<CameraProfile> { first, second },
                SelectedCameraId = first.Id
            },
            new[] { taskA, taskB },
            taskA);

        vm.Cameras[0].PrimaryTaskId = taskA.Id;
        vm.Cameras[1].PrimaryTaskId = taskB.Id;

        Assert.True(vm.TrySave(), vm.ErrorMessage);

        var saved = CameraSettingsStorage.Load(configPath);
        Assert.Collection(
            saved.Cameras,
            camera => Assert.Equal(taskA.Id, camera.PrimaryTaskId),
            camera => Assert.Equal(taskB.Id, camera.PrimaryTaskId));
    }

    [Fact]
    public void RemoveCamera_DoesNotRemoveLastCamera()
    {
        using var context = new DesktopCoordinatorTestContext();
        var configPath = Path.Combine(context.RootDirectory, "camera_config.json");
        var camera = CameraProfile.CreateDefault(1);
        camera.Id = "cam-1";
        var vm = CreateViewModel(
            configPath,
            new CameraSettings
            {
                Cameras = new List<CameraProfile> { camera },
                SelectedCameraId = camera.Id
            },
            Array.Empty<VisionTaskDefinition>(),
            initialTask: null);

        Assert.False(vm.RemoveCameraCommand.CanExecute(null));
        vm.RemoveCameraCommand.Execute(null);

        Assert.Single(vm.Cameras);
        Assert.NotNull(vm.SelectedCamera);
        Assert.Equal("cam-1", vm.SelectedCamera!.Id);
        Assert.True(vm.HasSelectedCamera);
    }

    [Fact]
    public void RemoveCamera_SelectsNeighborBeforeRemovingCurrentCamera()
    {
        using var context = new DesktopCoordinatorTestContext();
        var configPath = Path.Combine(context.RootDirectory, "camera_config.json");
        var first = CameraProfile.CreateDefault(1);
        first.Id = "cam-1";
        var second = CameraProfile.CreateDefault(2);
        second.Id = "cam-2";
        var vm = CreateViewModel(
            configPath,
            new CameraSettings
            {
                Cameras = new List<CameraProfile> { first, second },
                SelectedCameraId = first.Id
            },
            Array.Empty<VisionTaskDefinition>(),
            initialTask: null);

        Assert.True(vm.RemoveCameraCommand.CanExecute(null));
        vm.RemoveCameraCommand.Execute(null);

        Assert.Single(vm.Cameras);
        Assert.Equal("cam-2", vm.SelectedCamera?.Id);
        Assert.True(vm.HasSelectedCamera);
        Assert.False(vm.RemoveCameraCommand.CanExecute(null));
    }

    [Fact]
    public void CameraSettingsStorage_LoadInvalidJson_DoesNotOverwriteFile()
    {
        using var context = new DesktopCoordinatorTestContext();
        var configPath = Path.Combine(context.RootDirectory, "camera_config.json");
        const string brokenJson = "{ invalid";
        File.WriteAllText(configPath, brokenJson);

        Assert.Throws<InvalidOperationException>(() => CameraSettingsStorage.Load(configPath));

        Assert.Equal(brokenJson, File.ReadAllText(configPath));
    }

    [Fact]
    public void CameraSettingsStorage_SaveSopProfiles_PreservesCameraBindings()
    {
        using var context = new DesktopCoordinatorTestContext();
        var configPath = Path.Combine(context.RootDirectory, "camera_config.json");
        var camera = CameraProfile.CreateDefault(1);
        camera.PrimaryTaskId = "detector-a";
        camera.SelectedSopProfileId = "sop-a";
        CameraSettingsStorage.Save(
            configPath,
            new CameraSettings
            {
                Cameras = new List<CameraProfile> { camera },
                SelectedCameraId = camera.Id
            });

        CameraSettingsStorage.SaveSopProfiles(
            configPath,
            new[]
            {
                new CameraSopProfile
                {
                    Id = "sop-a",
                    Name = "SOP A",
                    Strategy = AnalysisStrategyNames.SopRules,
                    Steps = new List<CameraSopStep>
                    {
                        new() { Step = 1, Name = "Step A" }
                    }
                }
            });

        var saved = CameraSettingsStorage.Load(configPath);
        var savedCamera = Assert.Single(saved.Cameras);
        Assert.Equal("detector-a", savedCamera.PrimaryTaskId);
        Assert.Equal("sop-a", savedCamera.SelectedSopProfileId);
        Assert.Equal("sop-a", Assert.Single(saved.SopProfiles).Id);
    }

    [Fact]
    public void CameraSettingsStorage_Load_NormalizesLegacySop2Profile()
    {
        using var context = new DesktopCoordinatorTestContext();
        var configPath = Path.Combine(context.RootDirectory, "camera_config.json");

        CameraSettingsStorage.Save(
            configPath,
            new CameraSettings
            {
                Cameras = new List<CameraProfile> { CameraProfile.CreateDefault(1) },
                SopProfiles = new List<CameraSopProfile>
                {
                    CreateLegacySop2Profile()
                }
            });

        var saved = CameraSettingsStorage.Load(configPath);
        var sop2 = Assert.Single(saved.SopProfiles);
        Assert.Equal(AnalysisStrategyNames.Sop2, sop2.Strategy);
        Assert.Equal(6, sop2.Steps.Count);
        Assert.DoesNotContain(sop2.Steps, step => string.Equals(step.Name, "压紧", StringComparison.Ordinal));
    }

    [Fact]
    public void CameraSettingsStorage_Load_NormalizesLegacySop1Profile()
    {
        using var context = new DesktopCoordinatorTestContext();
        var configPath = Path.Combine(context.RootDirectory, "camera_config.json");

        CameraSettingsStorage.Save(
            configPath,
            new CameraSettings
            {
                Cameras = new List<CameraProfile> { CameraProfile.CreateDefault(1) },
                SopProfiles = new List<CameraSopProfile>
                {
                    CreateLegacySop1Profile()
                }
            });

        var saved = CameraSettingsStorage.Load(configPath);
        var sop1 = Assert.Single(saved.SopProfiles);
        Assert.Equal(AnalysisStrategyNames.Sop1, sop1.Strategy);
        Assert.Equal(5, sop1.Steps.Count);
        Assert.DoesNotContain(sop1.Steps, step => string.Equals(step.Name, "Extra", StringComparison.Ordinal));
    }

    [Fact]
    public void SopConfigViewModel_StrategyOptionsIncludeSop1()
    {
        var vm = new SopConfigViewModel(Array.Empty<SopProfile>());

        Assert.Contains(AnalysisStrategyNames.Sop1, vm.StrategyOptions);
    }

    [Fact]
    public void CameraSettingsRepository_RoundTripsCameraAndSopConfiguration()
    {
        using var context = new DesktopCoordinatorTestContext();
        var dbPath = Path.Combine(context.RootDirectory, "workspace.db");
        var repository = new CameraSettingsRepository(dbPath);
        var camera = CameraProfile.CreateDefault(1);
        camera.Name = "Camera A";
        camera.PrimaryTaskId = "detector-a";
        camera.SelectedSopProfileId = "sop-a";
        camera.EnableSopAnalysis = true;
        camera.SopWindowMs = 2400;

        repository.Save(new CameraSettings
        {
            Cameras = new List<CameraProfile> { camera },
            SelectedCameraId = camera.Id,
            SopProfiles = new List<CameraSopProfile>
            {
                new()
                {
                    Id = "sop-a",
                    Name = "SOP A",
                    Strategy = AnalysisStrategyNames.SopRules,
                    FingerprintModuleId = "fingerprint-1",
                    Steps = new List<CameraSopStep>
                    {
                        new() { Step = 1, Name = "Pick", ActionCode = "pick" },
                        new() { Step = 2, Name = "Place", ExpectedStateCode = "placed" }
                    }
                }
            }
        });

        var saved = repository.Load();
        var savedCamera = Assert.Single(saved.Cameras);
        Assert.Equal(camera.Id, saved.SelectedCameraId);
        Assert.Equal("detector-a", savedCamera.PrimaryTaskId);
        Assert.Equal("sop-a", savedCamera.SelectedSopProfileId);
        Assert.True(savedCamera.EnableSopAnalysis);
        Assert.Equal(2400, savedCamera.SopWindowMs);
        var savedSop = Assert.Single(saved.SopProfiles);
        Assert.Equal("fingerprint-1", savedSop.FingerprintModuleId);
        Assert.Collection(
            savedSop.Steps,
            step => Assert.Equal("pick", step.ActionCode),
            step => Assert.Equal("placed", step.ExpectedStateCode));
    }

    [Fact]
    public void CameraSettingsRepository_Load_MigratesLegacySop2Profile()
    {
        using var context = new DesktopCoordinatorTestContext();
        var repository = new CameraSettingsRepository(Path.Combine(context.RootDirectory, "workspace.db"));
        var camera = CameraProfile.CreateDefault(1);
        camera.SelectedSopProfileId = "sop2-legacy";
        camera.EnableSopAnalysis = true;
        repository.Save(new CameraSettings
        {
            Cameras = new List<CameraProfile> { camera },
            SelectedCameraId = camera.Id,
            SopProfiles = new List<CameraSopProfile>
            {
                CreateLegacySop2Profile()
            }
        });

        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        DbSession.ConfigDb.Updateable<SopProfileEntity>()
            .SetColumns(profile => new SopProfileEntity
            {
                Strategy = AnalysisStrategyNames.SopRules,
                UpdatedUtcMs = nowUtcMs
            })
            .Where(profile => profile.Id == "sop2-legacy")
            .ExecuteCommand();
        DbSession.ConfigDb.Insertable(new SopStepEntity
        {
            ProfileId = "sop2-legacy",
            Step = 7,
            Name = "压紧",
            CreatedUtcMs = nowUtcMs,
            UpdatedUtcMs = nowUtcMs
        }).ExecuteCommand();

        var loaded = repository.Load();

        var sop2 = Assert.Single(loaded.SopProfiles);
        Assert.Equal(AnalysisStrategyNames.Sop2, sop2.Strategy);
        Assert.Equal(6, sop2.Steps.Count);

        var persisted = Assert.Single(DbSession.ConfigDb.Queryable<SopProfileEntity>().ToList());
        Assert.Equal(AnalysisStrategyNames.Sop2, persisted.Strategy);
        Assert.Equal(6, DbSession.ConfigDb.Queryable<SopStepEntity>().Count());
    }

    [Fact]
    public void CameraSettingsRepository_Load_MigratesLegacySop1Profile()
    {
        using var context = new DesktopCoordinatorTestContext();
        var repository = new CameraSettingsRepository(Path.Combine(context.RootDirectory, "workspace.db"));
        var camera = CameraProfile.CreateDefault(1);
        camera.SelectedSopProfileId = "sop1-legacy";
        camera.EnableSopAnalysis = true;
        repository.Save(new CameraSettings
        {
            Cameras = new List<CameraProfile> { camera },
            SelectedCameraId = camera.Id,
            SopProfiles = new List<CameraSopProfile>
            {
                CreateLegacySop1Profile()
            }
        });

        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        DbSession.ConfigDb.Updateable<SopProfileEntity>()
            .SetColumns(profile => new SopProfileEntity
            {
                Strategy = AnalysisStrategyNames.SopRules,
                UpdatedUtcMs = nowUtcMs
            })
            .Where(profile => profile.Id == "sop1-legacy")
            .ExecuteCommand();
        DbSession.ConfigDb.Insertable(new SopStepEntity
        {
            ProfileId = "sop1-legacy",
            Step = 6,
            Name = "Extra",
            CreatedUtcMs = nowUtcMs,
            UpdatedUtcMs = nowUtcMs
        }).ExecuteCommand();

        var loaded = repository.Load();

        var sop1 = Assert.Single(loaded.SopProfiles);
        Assert.Equal(AnalysisStrategyNames.Sop1, sop1.Strategy);
        Assert.Equal(5, sop1.Steps.Count);

        var persisted = Assert.Single(DbSession.ConfigDb.Queryable<SopProfileEntity>().ToList());
        Assert.Equal(AnalysisStrategyNames.Sop1, persisted.Strategy);
        Assert.Equal(5, DbSession.ConfigDb.Queryable<SopStepEntity>().Count());
    }

    [Fact]
    public void CameraSettingsRepository_ImportFromFileIfEmpty_DoesNotOverwriteExistingDatabase()
    {
        using var context = new DesktopCoordinatorTestContext();
        var configPath = Path.Combine(context.RootDirectory, "camera_config.json");
        var repository = new CameraSettingsRepository(Path.Combine(context.RootDirectory, "workspace.db"));
        var dbCamera = CameraProfile.CreateDefault(1);
        dbCamera.Name = "DB Camera";
        repository.Save(new CameraSettings
        {
            Cameras = new List<CameraProfile> { dbCamera },
            SelectedCameraId = dbCamera.Id
        });

        var fileCamera = CameraProfile.CreateDefault(1);
        fileCamera.Name = "File Camera";
        CameraSettingsStorage.Save(
            configPath,
            new CameraSettings
            {
                Cameras = new List<CameraProfile> { fileCamera },
                SelectedCameraId = fileCamera.Id
            });

        repository.ImportFromFileIfEmpty(configPath);

        Assert.Equal("DB Camera", Assert.Single(repository.Load().Cameras).Name);
    }

    [Fact]
    public void CameraProfileViewModel_TryBuild_PreservesHiddenProfileFields()
    {
        var profile = CameraProfile.CreateDefault(1);
        profile.OpenCvSource = "rtsp://example.local/stream";
        profile.OpenCvBackend = "ffmpeg";
        profile.RecordingCodecFourcc = "XVID";
        profile.RecordingQueueCapacity = 240;
        profile.RecordingFps = 12.5;

        var vm = new CameraProfileViewModel(profile, new CameraProviderRegistry(Array.Empty<CameraProviderRegistration>()));
        vm.Name = "Updated Camera";
        vm.TargetFpsText = "12";

        Assert.True(vm.TryBuild(out var built, out var error), error);
        Assert.Equal("rtsp://example.local/stream", built.OpenCvSource);
        Assert.Equal("ffmpeg", built.OpenCvBackend);
        Assert.Equal("XVID", built.RecordingCodecFourcc);
        Assert.Equal(240, built.RecordingQueueCapacity);
        Assert.Equal(12.5, built.RecordingFps);
    }

    [Fact]
    public void CameraProfileViewModel_TryBuild_PreservesPrimaryTaskId()
    {
        var profile = CameraProfile.CreateDefault(1);
        profile.PrimaryTaskId = "detector-a";

        var vm = new CameraProfileViewModel(profile, new CameraProviderRegistry(Array.Empty<CameraProviderRegistration>()));
        vm.PrimaryTaskId = "detector-b";

        Assert.True(vm.TryBuild(out var built, out var error), error);
        Assert.Equal("detector-b", built.PrimaryTaskId);
    }

    private static CameraConfigViewModel CreateViewModel(
        string configPath,
        CameraSettings settings,
        IReadOnlyList<VisionTaskDefinition> tasks,
        VisionTaskDefinition? initialTask)
    {
        return new CameraConfigViewModel(
            configPath,
            settings,
            new CameraProviderRegistry(Array.Empty<CameraProviderRegistration>()),
            new StubEnvironmentDiagnosticsService(),
            tasks,
            initialTask,
            Path.GetDirectoryName(configPath)!,
            () => tasks,
            () => { },
            Array.Empty<SopProfile>());
    }

    private static VisionTaskDefinition CreateTask(string id, string displayName)
    {
        return new VisionTaskDefinition
        {
            Id = id,
            DisplayName = displayName,
            TaskKind = VisionTaskKind.Detection,
            RuntimeKind = VisionRuntimeKind.OnnxRuntime,
            BundlePath = string.Empty,
            ConfigPath = string.Empty,
            Metadata = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>())
        };
    }

    private static CameraSopProfile CreateLegacySop2Profile()
    {
        return new CameraSopProfile
        {
            Id = "sop2-legacy",
            Name = "sop2",
            Strategy = AnalysisStrategyNames.SopRules,
            Steps = Enumerable.Range(1, 7)
                .Select(step => new CameraSopStep
                {
                    Step = step,
                    Name = step == 7 ? "压紧" : $"Step {step}"
                })
                .ToList()
        };
    }

    private static CameraSopProfile CreateLegacySop1Profile()
    {
        return new CameraSopProfile
        {
            Id = "sop1-legacy",
            Name = "sop1",
            Strategy = AnalysisStrategyNames.SopRules,
            Steps = Enumerable.Range(1, 6)
                .Select(step => new CameraSopStep
                {
                    Step = step,
                    Name = step == 6 ? "Extra" : $"Step {step}"
                })
                .ToList()
        };
    }

    private sealed class StubEnvironmentDiagnosticsService : IEnvironmentDiagnosticsService
    {
        public EnvironmentDiagnosticsResult Run()
        {
            return new EnvironmentDiagnosticsResult(EnvironmentDiagnosticsState.Success, "OK", string.Empty);
        }
    }
}
