using System.Text.Json;

namespace VideoInferenceDemo;

public sealed class HardwareSettingsRepository
{
    private const string GlobalDbConfigKey = "db_config";
    private const string GlobalAnalysisConfigKey = "analysis_config";

    public HardwareSettingsRepository(string dbPath)
    {
    }

    public bool HasGlobalConfig()
    {
        return LoadStateValue(GlobalDbConfigKey) != null;
    }

    public DbConfig LoadDbConfig()
    {
        var json = LoadStateValue(GlobalDbConfigKey);
        if (string.IsNullOrWhiteSpace(json))
            return new DbConfig();
        return (JsonSerializer.Deserialize<DbConfig>(json) ?? new DbConfig()).Normalize();
    }

    public AnalysisConfig LoadAnalysisConfig()
    {
        var json = LoadStateValue(GlobalAnalysisConfigKey);
        if (string.IsNullOrWhiteSpace(json))
            return new AnalysisConfig();
        return JsonSerializer.Deserialize<AnalysisConfig>(json) ?? new AnalysisConfig();
    }

    public void ImportFromAppConfigIfEmpty(AppConfig appConfig)
    {
        var fingerprintModules = appConfig.FingerprintModules ?? new List<FingerprintModuleOptions>();
        var modbusModules = appConfig.ModbusModules ?? new List<ModbusModuleOptions>();

        DbSession.ConfigDb.Ado.UseTran(() =>
        {
            if (DbSession.ConfigDb.Queryable<CameraSettingsStateEntity>().Count() == 0)
            {
                if (DbSession.ConfigDb.Queryable<FingerprintModuleEntity>().Count() == 0 &&
                    fingerprintModules.Count > 0)
                {
                    ReplaceFingerprintModules(fingerprintModules);
                }

                if (DbSession.ConfigDb.Queryable<ModbusModuleEntity>().Count() == 0 &&
                    modbusModules.Count > 0)
                {
                    ReplaceModbusModules(modbusModules);
                }

                if (LoadStateValue(GlobalDbConfigKey) == null)
                {
                    SaveStateValue(GlobalDbConfigKey,
                        JsonSerializer.Serialize((appConfig.Db ?? new DbConfig()).Normalize()));
                }

                if (LoadStateValue(GlobalAnalysisConfigKey) == null)
                {
                    SaveStateValue(GlobalAnalysisConfigKey,
                        JsonSerializer.Serialize(appConfig.Analysis ?? new AnalysisConfig()));
                }
            }
        });
    }

    public IReadOnlyList<FingerprintModuleOptions> LoadFingerprintModules()
    {
        var entities = DbSession.ConfigDb.Queryable<FingerprintModuleEntity>()
            .OrderBy("sort_order, id")
            .ToList();

        return entities.Select(e => new FingerprintModuleOptions
        {
            Id = e.Id,
            Name = e.Name,
            Enabled = e.Enabled != 0,
            ConnectionKind = Enum.TryParse<FingerprintConnectionKind>(e.ConnectionKind, ignoreCase: true, out var kind) ? kind : FingerprintConnectionKind.SerialRtu,
            SlaveAddress = (byte)e.SlaveAddress,
            PortName = e.PortName,
            BaudRate = e.BaudRate,
            DataBits = e.DataBits,
            Parity = e.Parity,
            StopBits = e.StopBits,
            Host = e.Host,
            TcpPort = e.TcpPort,
            ReadTimeoutMs = e.ReadTimeoutMs,
            WriteTimeoutMs = e.WriteTimeoutMs,
            PollIntervalMs = e.PollIntervalMs,
            DuplicateSuppressMs = e.DuplicateSuppressMs
        }.Normalize()).ToList();
    }

    public IReadOnlyList<ModbusModuleOptions> LoadModbusModules()
    {
        var entities = DbSession.ConfigDb.Queryable<ModbusModuleEntity>()
            .OrderBy("sort_order, id")
            .ToList();

        return entities.Select(e =>
        {
            var module = new ModbusModuleOptions
            {
                Id = e.Id,
                Name = e.Name,
                Enabled = e.Enabled != 0,
                Host = e.Host,
                Port = e.Port,
                SlaveAddress = (byte)e.SlaveAddress,
                PollIntervalMs = e.PollIntervalMs,
                OutputStartAddress = (ushort)e.OutputStartAddress,
                InputStartAddress = (ushort)e.InputStartAddress
            }.Normalize();

            module.Lights = LoadLightBindings(module.Id).ToList();
            return module;
        }).ToList();
    }

    public void Save(
        IReadOnlyList<FingerprintModuleOptions> fingerprintModules,
        IReadOnlyList<ModbusModuleOptions> modbusModules)
    {
        DbSession.ConfigDb.Ado.UseTran(() =>
        {
            ReplaceFingerprintModules(fingerprintModules);
            ReplaceModbusModules(modbusModules);
        });
    }

    public void SaveGlobalConfig(DbConfig dbConfig, AnalysisConfig analysisConfig)
    {
        SaveStateValue(GlobalDbConfigKey,
            JsonSerializer.Serialize((dbConfig ?? new DbConfig()).Normalize()));
        SaveStateValue(GlobalAnalysisConfigKey,
            JsonSerializer.Serialize(analysisConfig ?? new AnalysisConfig()));
    }

    private string? LoadStateValue(string key)
    {
        return DbSession.ConfigDb.Queryable<CameraSettingsStateEntity>()
            .Where(e => e.Key == key)
            .First()?.Value;
    }

    private void SaveStateValue(string key, string value)
    {
        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        DbSession.ConfigDb.Storageable(new CameraSettingsStateEntity
        {
            Key = key,
            Value = value,
            UpdatedUtcMs = nowUtcMs
        }).ExecuteCommand();
    }

    private void ReplaceFingerprintModules(IReadOnlyList<FingerprintModuleOptions> modules)
    {
        DbSession.ConfigDb.Deleteable<FingerprintModuleEntity>().ExecuteCommand();
        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var index = 0;
        var entities = modules
            .Where(m => m != null)
            .Select(m => m.Normalize())
            .Select(m => new FingerprintModuleEntity
            {
                Id = m.Id,
                Name = m.Name,
                Enabled = m.Enabled ? 1 : 0,
                ConnectionKind = m.ConnectionKind.ToString(),
                SlaveAddress = m.SlaveAddress,
                PortName = m.PortName,
                BaudRate = m.BaudRate,
                DataBits = m.DataBits,
                Parity = m.Parity,
                StopBits = m.StopBits,
                Host = m.Host,
                TcpPort = m.TcpPort,
                ReadTimeoutMs = m.ReadTimeoutMs,
                WriteTimeoutMs = m.WriteTimeoutMs,
                PollIntervalMs = m.PollIntervalMs,
                DuplicateSuppressMs = m.DuplicateSuppressMs,
                SortOrder = index++,
                CreatedUtcMs = nowUtcMs,
                UpdatedUtcMs = nowUtcMs
            }).ToList();
        if (entities.Count > 0)
            DbSession.ConfigDb.Insertable(entities).ExecuteCommand();
    }

    private void ReplaceModbusModules(IReadOnlyList<ModbusModuleOptions> modules)
    {
        DbSession.ConfigDb.Deleteable<ModbusModuleEntity>().ExecuteCommand();
        DbSession.ConfigDb.Deleteable<ThreeColorLightBindingEntity>().ExecuteCommand();
        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var index = 0;
        foreach (var module in modules.Where(m => m != null).Select(m => m.Normalize()))
        {
            DbSession.ConfigDb.Insertable(new ModbusModuleEntity
            {
                Id = module.Id,
                Name = module.Name,
                Enabled = module.Enabled ? 1 : 0,
                Host = module.Host,
                Port = module.Port,
                SlaveAddress = module.SlaveAddress,
                PollIntervalMs = module.PollIntervalMs,
                OutputStartAddress = module.OutputStartAddress,
                InputStartAddress = module.InputStartAddress,
                SortOrder = index++,
                CreatedUtcMs = nowUtcMs,
                UpdatedUtcMs = nowUtcMs
            }).ExecuteCommand();

            var lightIndex = 0;
            foreach (var light in module.Lights.Where(l => l != null))
            {
                DbSession.ConfigDb.Insertable(new ThreeColorLightBindingEntity
                {
                    ModuleId = module.Id,
                    LightNumber = light.LightNumber,
                    RedChannelNumber = light.RedChannelNumber,
                    GreenChannelNumber = light.GreenChannelNumber,
                    BuzzerChannelNumber = light.BuzzerChannelNumber,
                    BuzzerEnabled = light.BuzzerEnabled ? 1 : 0,
                    SortOrder = lightIndex++,
                    CreatedUtcMs = nowUtcMs,
                    UpdatedUtcMs = nowUtcMs
                }).ExecuteCommand();
            }
        }
    }

    private IReadOnlyList<ThreeColorLightBinding> LoadLightBindings(string moduleId)
    {
        return DbSession.ConfigDb.Queryable<ThreeColorLightBindingEntity>()
            .Where(e => e.ModuleId == moduleId)
            .OrderBy("sort_order, id")
            .ToList()
            .Select(e => new ThreeColorLightBinding
            {
                LightNumber = e.LightNumber,
                RedChannelNumber = e.RedChannelNumber,
                GreenChannelNumber = e.GreenChannelNumber,
                BuzzerChannelNumber = e.BuzzerChannelNumber,
                BuzzerEnabled = e.BuzzerEnabled != 0
            })
            .ToList();
    }
}
