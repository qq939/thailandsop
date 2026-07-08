using Xunit;

namespace VideoInferenceDemo.Tests.Hardware;

[Collection("DbSession")]
public sealed class FingerprintModuleTests
{
    [Fact]
    public async Task ReadRecognitionResult_ReturnsMatchedIdAndScore()
    {
        using var client = new FakeModbusRegisterClient();
        client.Set(FingerprintModuleRegisters.RecognitionResult, 7);
        client.Set(FingerprintModuleRegisters.MatchScore, 92);
        using var module = new FingerprintModule(client, slaveAddress: 1);

        var result = await module.ReadRecognitionResultAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(7, result.FingerprintId);
        Assert.Equal(92, result.Score);
    }

    [Fact]
    public async Task SetLight_WritesModeAndColorBytes()
    {
        using var client = new FakeModbusRegisterClient();
        using var module = new FingerprintModule(client, slaveAddress: 1);

        await module.SetLightAsync(FingerprintLightMode.Breathing, FingerprintLightColor.Red, CancellationToken.None);

        Assert.Contains(client.Writes, item =>
            item.SlaveAddress == 1 &&
            item.Address == FingerprintModuleRegisters.LightRing &&
            item.Value == 0x0101);
    }

    [Fact]
    public void PersonnelRepository_BindsAndFindsFingerprintId()
    {
        var root = Path.Combine(Path.GetTempPath(), "VideoInferenceDemo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "workspace.db");
        try
        {
            DbSession.Initialize(dbPath);
            var repository = new PersonnelRepository(dbPath);
            repository.Upsert("E001", "Operator A", fingerprintId: 12);

            var found = repository.GetByFingerprintId(12);

            Assert.NotNull(found);
            Assert.Equal("E001", found!.EmployeeCode);
            Assert.Equal(12, found.FingerprintId);
        }
        finally
        {
            DbSession.Reset();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void SopAlarmEventRepository_RecordsAlarmAndFingerprintReset()
    {
        var root = Path.Combine(Path.GetTempPath(), "VideoInferenceDemo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "workspace.db");
        try
        {
            DbSession.Initialize(dbPath);
            var personnelRepository = new PersonnelRepository(dbPath);
            personnelRepository.Upsert("E001", "Operator A", fingerprintId: 12);
            var repository = new SopAlarmEventRepository(dbPath);
            const string runUuid = "run-1";
            const string alarmUuid = "alarm-1";

            repository.Insert(new SopAlarmEventRecord(
                alarmUuid,
                SopAlarmEventTypes.Alarm,
                runUuid,
                "camera-1",
                "Camera 1",
                "camera:camera-1",
                2,
                "装适配器步骤检测到错误对象",
                string.Empty,
                string.Empty,
                string.Empty,
                null,
                string.Empty,
                string.Empty,
                100,
                string.Empty,
                string.Empty));
            repository.Insert(new SopAlarmEventRecord(
                "reset-1",
                SopAlarmEventTypes.Reset,
                runUuid,
                "camera-1",
                "Camera 1",
                "camera:camera-1",
                2,
                "装适配器步骤检测到错误对象",
                "fingerprint",
                "fingerprint-1",
                "指纹模块1",
                12,
                "E001",
                "Operator A",
                200,
                alarmUuid,
                "fingerprint reset"));

            var rows = repository.ListByRunUuid(runUuid);

            Assert.Equal(2, rows.Count);
            Assert.Equal(SopAlarmEventTypes.Alarm, rows[0].EventType);
            Assert.Equal(SopAlarmEventTypes.Reset, rows[1].EventType);
            Assert.Equal(alarmUuid, rows[1].RelatedAlarmEventUuid);
            Assert.Equal("fingerprint", rows[1].ResetSource);
            Assert.Equal(12, rows[1].FingerprintId);
            Assert.Equal("E001", rows[1].EmployeeCode);
        }
        finally
        {
            DbSession.Reset();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void PersonnelRepository_SetFingerprintBinding_AlsoUpdatesPersonnelFingerprintId()
    {
        var root = Path.Combine(Path.GetTempPath(), "VideoInferenceDemo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "workspace.db");
        try
        {
            DbSession.Initialize(dbPath);
            var repository = new PersonnelRepository(dbPath);
            repository.Upsert("E001", "Operator A");

            repository.SetFingerprintBinding("E001", "fingerprint-1", 12);

            Assert.Equal(12, repository.GetFingerprintBinding("E001", "fingerprint-1"));
            Assert.Equal(12, repository.GetByCode("E001")?.FingerprintId);
            Assert.Equal(12, repository.List(includeInactive: true).Single().FingerprintId);
        }
        finally
        {
            DbSession.Reset();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void PersonnelRepository_NormalizeFingerprintBindingsForSingleModule_MigratesLegacyModuleId()
    {
        var root = Path.Combine(Path.GetTempPath(), "VideoInferenceDemo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "fingerprint.db");
        try
        {
            DbSession.Initialize(dbPath);
            var repository = new PersonnelRepository(dbPath);
            repository.Upsert("E001", "Operator A");
            repository.SetFingerprintBinding("E001", "fingerprint-1", 12);

            repository.NormalizeFingerprintBindingsForModules(new[] { "module-current" });

            Assert.Null(repository.GetFingerprintBinding("E001", "fingerprint-1"));
            Assert.Equal(12, repository.GetFingerprintBinding("E001", "module-current"));
            Assert.Equal(12, repository.GetByCode("E001")?.FingerprintId);
            Assert.Equal("E001", repository.GetByFingerprintId(12, "module-current")?.EmployeeCode);
        }
        finally
        {
            DbSession.Reset();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void FingerprintRuntimeModuleSelector_EnablesSopBoundModuleForRuntime()
    {
        var modules = new[]
        {
            new FingerprintModuleOptions
            {
                Enabled = false,
                Id = "fingerprint-1",
                Name = "指纹模块1"
            },
            new FingerprintModuleOptions
            {
                Enabled = false,
                Id = "fingerprint-2",
                Name = "指纹模块2"
            }
        };
        var sopProfiles = new[]
        {
            new SopProfile
            {
                Id = "sop-default",
                Name = "默认组装 SOP",
                FingerprintModuleId = "fingerprint-1"
            }
        };

        var runtimeModules = FingerprintRuntimeModuleSelector.SelectRuntimeModules(modules, sopProfiles);

        Assert.True(runtimeModules.Single(item => item.Id == "fingerprint-1").Enabled);
        Assert.False(runtimeModules.Single(item => item.Id == "fingerprint-2").Enabled);
    }

    [Fact]
    public void SopConfigViewModel_SavesFingerprintModuleBinding()
    {
        var root = Path.Combine(Path.GetTempPath(), "VideoInferenceDemo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "app_config.json");
        try
        {
            var fingerprintModules = new List<FingerprintModuleOptions>();
            fingerprintModules.Add(new FingerprintModuleOptions
            {
                Enabled = true,
                Id = "fingerprint-1",
                Name = "指纹模块1"
            });
            var existingProfiles = new List<SopProfile>
            {
                new()
                {
                    Id = "sop-default",
                    Name = "Default SOP",
                    Steps = new List<FsmStepDefinition>
                    {
                        new() { Step = 1, Name = "Step 1" }
                    }
                }
            };
            IReadOnlyList<CameraSopProfile>? savedProfiles = null;

            var vm = new SopConfigViewModel(
                existingProfiles,
                fingerprintModules,
                profiles => savedProfiles = profiles.ToList());

            Assert.Contains(vm.FingerprintModuleOptions, item => item.Id == "fingerprint-1");
            Assert.NotNull(vm.SelectedProfile);
            vm.SelectedProfile!.FingerprintModuleId = "fingerprint-1";

            Assert.True(vm.TrySave(), vm.ErrorMessage);

            Assert.NotNull(savedProfiles);
            Assert.Equal("fingerprint-1", savedProfiles![0].FingerprintModuleId);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void HardwareSettingsRepository_SaveAndLoadGlobalConfig()
    {
        var root = Path.Combine(Path.GetTempPath(), "VideoInferenceDemo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "workspace.db");
        try
        {
            DbSession.Initialize(dbPath);
            var repository = new HardwareSettingsRepository(dbPath);

            Assert.False(repository.HasGlobalConfig());

            var savedDb = new DbConfig { EnableRawDetections = true, EnableTcnFeatures = true };
            var savedAnalysis = new AnalysisConfig { EnableOnlineAnalysis = true, FrameWindowSize = 10, HoldFrames = 5 };
            repository.SaveGlobalConfig(savedDb, savedAnalysis);

            Assert.True(repository.HasGlobalConfig());
            Assert.True(repository.LoadDbConfig().EnableRawDetections);
            Assert.True(repository.LoadDbConfig().EnableTcnFeatures);
            Assert.False(repository.LoadDbConfig().EnableTcnInference);
            Assert.True(repository.LoadAnalysisConfig().EnableOnlineAnalysis);
            Assert.Equal(10, repository.LoadAnalysisConfig().FrameWindowSize);
            Assert.Equal(5, repository.LoadAnalysisConfig().HoldFrames);

            // Verify defaults for not-yet-saved fields
            var reloadedAnalysis = repository.LoadAnalysisConfig();
            Assert.Equal(0, reloadedAnalysis.SopWindowMs);
        }
        finally
        {
            DbSession.Reset();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void PersonnelManagementViewModel_AllowsEnrollmentWithConfiguredDisabledModule()
    {
        var root = Path.Combine(Path.GetTempPath(), "VideoInferenceDemo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "workspace.db");
        try
        {
            DbSession.Initialize(dbPath);
            var repository = new PersonnelRepository(dbPath);
            repository.Upsert("E001", "Operator A");
            var vm = new PersonnelManagementViewModel(
                repository,
                fingerprintModules:
                [
                    new FingerprintModuleOptions
                    {
                        Enabled = false,
                        Id = "fingerprint-1",
                        Name = "指纹模块1"
                    }
                ]);

            Assert.Single(vm.AvailableModules);
            Assert.NotNull(vm.SelectedFingerprintModule);
            Assert.NotNull(vm.SelectedPersonnel);
            Assert.True(vm.EnrollFingerprintCommand.CanExecute(vm.SelectedPersonnel));
        }
        finally
        {
            DbSession.Reset();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class FakeModbusRegisterClient : IModbusRegisterClient
    {
        private readonly Dictionary<ushort, ushort> _registers = new();

        public List<(byte SlaveAddress, ushort Address, ushort Value)> Writes { get; } = new();

        public void Set(ushort address, ushort value)
        {
            _registers[address] = value;
        }

        public Task<ushort[]> ReadHoldingRegistersAsync(byte slaveAddress, ushort startAddress, ushort numberOfPoints, CancellationToken ct)
        {
            var values = new ushort[numberOfPoints];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = _registers.GetValueOrDefault((ushort)(startAddress + i), FingerprintModuleRegisters.NoResultValue);
            }

            return Task.FromResult(values);
        }

        public Task WriteSingleRegisterAsync(byte slaveAddress, ushort registerAddress, ushort value, CancellationToken ct)
        {
            Writes.Add((slaveAddress, registerAddress, value));
            _registers[registerAddress] = value;
            return Task.CompletedTask;
        }

        public Task WriteMultipleRegistersAsync(byte slaveAddress, ushort startAddress, ushort[] values, CancellationToken ct)
        {
            for (var i = 0; i < values.Length; i++)
            {
                var address = (ushort)(startAddress + i);
                Writes.Add((slaveAddress, address, values[i]));
                _registers[address] = values[i];
            }

            return Task.CompletedTask;
        }

        public Task WriteSingleCoilAsync(byte slaveAddress, ushort coilAddress, bool value, CancellationToken ct)
        {
            _registers[coilAddress] = value ? (ushort)0xFF00 : (ushort)0x0000;
            return Task.CompletedTask;
        }

        public Task WriteMultipleCoilsAsync(byte slaveAddress, ushort startAddress, bool[] values, CancellationToken ct)
        {
            for (var i = 0; i < values.Length; i++)
            {
                _registers[(ushort)(startAddress + i)] = values[i] ? (ushort)0xFF00 : (ushort)0x0000;
            }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
