using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using VideoInferenceDemo;

namespace VideoInferenceDemo.ImageInspection;

public static class InspectionCameraSettingsStorage
{
    private const string StateKey = "image_inspection_camera_settings";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static InspectionCameraSettings Load(string path)
    {
        try
        {
            if (DbSession.IsInitialized)
            {
                var state = LoadState();
                if (!string.IsNullOrWhiteSpace(state))
                {
                    return Normalize(JsonSerializer.Deserialize<InspectionCameraSettings>(state, Options));
                }

                var legacy = LoadLegacyJson(path);
                SaveState(legacy);
                return legacy;
            }

            return LoadLegacyJson(path);
        }
        catch
        {
            return InspectionCameraSettings.CreateDefault();
        }
    }

    public static void Save(string path, InspectionCameraSettings settings)
    {
        var normalized = Normalize(settings);
        if (DbSession.IsInitialized)
        {
            SaveState(normalized);
            return;
        }

        var json = JsonSerializer.Serialize(normalized, Options);
        File.WriteAllText(path, json);
    }

    private static InspectionCameraSettings LoadLegacyJson(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return InspectionCameraSettings.CreateDefault();
            }

            var json = File.ReadAllText(path);
            return Normalize(JsonSerializer.Deserialize<InspectionCameraSettings>(json, Options));
        }
        catch
        {
            return InspectionCameraSettings.CreateDefault();
        }
    }

    private static InspectionCameraSettings Normalize(InspectionCameraSettings? settings)
    {
        settings ??= new InspectionCameraSettings();
        settings.Cameras ??= [];

        var normalized = new List<InspectionCameraProfile>();
        var usedCameraIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < settings.Cameras.Count; index++)
        {
            var camera = (settings.Cameras[index] ?? InspectionCameraProfile.CreateDefault(index + 1)).Normalize(index + 1);
            if (!usedCameraIds.Add(camera.Id))
            {
                camera.Id = CreateUniqueCameraId(camera.Id, index + 1, usedCameraIds);
                usedCameraIds.Add(camera.Id);
            }

            normalized.Add(camera);
        }

        if (normalized.Count == 0)
        {
            normalized.Add(InspectionCameraProfile.CreateDefault(1));
        }

        var selectedId = settings.SelectedCameraId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(selectedId) ||
            normalized.All(camera => !string.Equals(camera.Id, selectedId, StringComparison.OrdinalIgnoreCase)))
        {
            selectedId = normalized[0].Id;
        }

        return new InspectionCameraSettings
        {
            Cameras = normalized,
            SelectedCameraId = selectedId
        };
    }

    private static string CreateUniqueCameraId(string baseId, int ordinal, HashSet<string> usedCameraIds)
    {
        var normalizedBaseId = string.IsNullOrWhiteSpace(baseId)
            ? $"camera-{Math.Max(1, ordinal)}"
            : baseId.Trim();
        var suffix = Math.Max(2, ordinal);
        var candidate = $"{normalizedBaseId}-{suffix}";
        while (usedCameraIds.Contains(candidate))
        {
            suffix++;
            candidate = $"{normalizedBaseId}-{suffix}";
        }

        return candidate;
    }

    private static string? LoadState()
    {
        return DbSession.ConfigDb.Queryable<CameraSettingsStateEntity>()
            .Where(entity => entity.Key == StateKey)
            .First()?.Value;
    }

    private static void SaveState(InspectionCameraSettings settings)
    {
        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var json = JsonSerializer.Serialize(Normalize(settings), Options);
        DbSession.ConfigDb.Storageable(new CameraSettingsStateEntity
        {
            Key = StateKey,
            Value = json,
            UpdatedUtcMs = nowUtcMs
        }).ExecuteCommand();
    }
}
