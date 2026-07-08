using System.Text.Json;
using VideoInferenceDemo.ImageInspection;
using VideoInferenceDemo.ImageInspection.Runtime;

namespace VideoInferenceDemo.Tests.Inspection;

[Collection("DbSession")]
public sealed class InspectionParameterSettingsStorageTests : IDisposable
{
    private readonly string _root;

    public InspectionParameterSettingsStorageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "inspection-parameter-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        DbSession.Reset();
    }

    [Fact]
    public void Load_MigratesLegacyJsonIntoConfigDatabase()
    {
        var legacyPath = Path.Combine(_root, "inspection_parameter_config.json");
        File.WriteAllText(
            legacyPath,
            JsonSerializer.Serialize(new InspectionParameterSettings
            {
                DatabaseProvider = "MySQL",
                ConnectionString = "Server=127.0.0.1;Port=3306;Database=legacy;Uid=root;Pwd=abc;"
            }));
        DbSession.InitializeSplit(Path.Combine(_root, "workspace_config.db"), Path.Combine(_root, "results"));

        var loaded = InspectionParameterSettingsStorage.Load(legacyPath);

        Assert.Equal("MySQL", loaded.DatabaseProvider);
        Assert.Contains("Database=legacy", loaded.ConnectionString);
        var state = DbSession.ConfigDb.Queryable<CameraSettingsStateEntity>()
            .Where(entity => entity.Key == "image_inspection_parameter_settings")
            .First();
        Assert.NotNull(state);
        Assert.Contains("Database=legacy", state.Value);
    }

    [Fact]
    public void Save_WhenDbInitialized_WritesConfigDatabaseOnly()
    {
        var legacyPath = Path.Combine(_root, "inspection_parameter_config.json");
        File.WriteAllText(legacyPath, "legacy");
        DbSession.InitializeSplit(Path.Combine(_root, "workspace_config.db"), Path.Combine(_root, "results"));

        InspectionParameterSettingsStorage.Save(
            legacyPath,
            new InspectionParameterSettings
            {
                DatabaseProvider = "MySQL",
                ConnectionString = "Server=127.0.0.1;Port=3306;Database=saved;Uid=root;Pwd=abc;",
                PlcTrigger = new InspectionPlcTriggerOptions
                {
                    Enabled = true,
                    Host = "192.168.1.20",
                    Port = 1502,
                    SlaveAddress = 1,
                    PollIntervalMs = 200,
                    StationId = string.Empty,
                    TaskId = "Station 1 Appearance",
                    TriggerRegisterAddress = 10,
                    DoneRegisterAddress = 11,
                    ResultRegisterAddress = 12,
                    ReadTimeoutMs = 1500,
                    WriteTimeoutMs = 1600
                }
            });

        Assert.Equal("legacy", File.ReadAllText(legacyPath));
        var loaded = InspectionParameterSettingsStorage.Load(legacyPath);
        Assert.Equal("MySQL", loaded.DatabaseProvider);
        Assert.Contains("Database=saved", loaded.ConnectionString);
        Assert.True(loaded.PlcTrigger.Enabled);
        Assert.Equal("192.168.1.20", loaded.PlcTrigger.Host);
        Assert.Equal(1502, loaded.PlcTrigger.Port);
        Assert.Equal(1, loaded.PlcTrigger.SlaveAddress);
        Assert.Equal(200, loaded.PlcTrigger.PollIntervalMs);
        Assert.Equal(string.Empty, loaded.PlcTrigger.StationId);
        Assert.Equal("Station 1 Appearance", loaded.PlcTrigger.TaskId);
        Assert.Equal((ushort)10, loaded.PlcTrigger.TriggerRegisterAddress);
        Assert.Equal((ushort)11, loaded.PlcTrigger.DoneRegisterAddress);
        Assert.Equal((ushort)12, loaded.PlcTrigger.ResultRegisterAddress);
        Assert.Equal(1500, loaded.PlcTrigger.ReadTimeoutMs);
        Assert.Equal(1600, loaded.PlcTrigger.WriteTimeoutMs);
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
}
