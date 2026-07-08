using System.IO;
using System.Text.Json;
using VideoInferenceDemo;

namespace VideoInferenceDemo.ImageInspection;

public static class InspectionParameterSettingsStorage
{
    private const string StateKey = "image_inspection_parameter_settings";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static InspectionParameterSettings Load(string path)
    {
        try
        {
            if (DbSession.IsInitialized)
            {
                var state = LoadState();
                if (!string.IsNullOrWhiteSpace(state))
                {
                    return (JsonSerializer.Deserialize<InspectionParameterSettings>(state, Options)
                            ?? InspectionParameterSettings.CreateDefault()).Normalize();
                }

                var legacy = LoadLegacyJson(path);
                SaveState(legacy);
                return legacy;
            }

            return LoadLegacyJson(path);
        }
        catch
        {
            return InspectionParameterSettings.CreateDefault().Normalize();
        }
    }

    public static void Save(string path, InspectionParameterSettings settings)
    {
        var normalized = (settings ?? InspectionParameterSettings.CreateDefault()).Normalize();
        if (DbSession.IsInitialized)
        {
            SaveState(normalized);
            return;
        }

        var json = JsonSerializer.Serialize(normalized, Options);
        File.WriteAllText(path, json);
    }

    private static InspectionParameterSettings LoadLegacyJson(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return InspectionParameterSettings.CreateDefault().Normalize();
            }

            var json = File.ReadAllText(path);
            return (JsonSerializer.Deserialize<InspectionParameterSettings>(json, Options)
                    ?? InspectionParameterSettings.CreateDefault()).Normalize();
        }
        catch
        {
            return InspectionParameterSettings.CreateDefault().Normalize();
        }
    }

    private static string? LoadState()
    {
        return DbSession.ConfigDb.Queryable<CameraSettingsStateEntity>()
            .Where(entity => entity.Key == StateKey)
            .First()?.Value;
    }

    private static void SaveState(InspectionParameterSettings settings)
    {
        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var json = JsonSerializer.Serialize((settings ?? InspectionParameterSettings.CreateDefault()).Normalize(), Options);
        DbSession.ConfigDb.Storageable(new CameraSettingsStateEntity
        {
            Key = StateKey,
            Value = json,
            UpdatedUtcMs = nowUtcMs
        }).ExecuteCommand();
    }
}
