using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VideoInferenceDemo;

namespace VideoInferenceDemo.ImageInspection;

public static class InspectionTaskSettingsStorage
{
    private const string StateKey = "image_inspection_task_settings";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static InspectionTaskSettings Load(
        string path,
        IReadOnlyList<InspectionCameraProfile>? cameras = null,
        InspectionRecipeEntry? seedRecipe = null)
    {
        return Load(path, cameras, seedRecipe, out _);
    }

    public static InspectionTaskSettings Load(
        string path,
        IReadOnlyList<InspectionCameraProfile>? cameras,
        InspectionRecipeEntry? seedRecipe,
        out bool triggerModesAdjusted)
    {
        try
        {
            if (DbSession.IsInitialized)
            {
                var state = LoadState();
                if (!string.IsNullOrWhiteSpace(state))
                {
                    return Normalize(JsonSerializer.Deserialize<InspectionTaskSettings>(state, Options), cameras, seedRecipe, out triggerModesAdjusted);
                }

                var legacy = LoadLegacyJson(path, cameras, seedRecipe, out triggerModesAdjusted);
                SaveState(legacy, cameras, seedRecipe);
                return legacy;
            }

            return LoadLegacyJson(path, cameras, seedRecipe, out triggerModesAdjusted);
        }
        catch
        {
            return Normalize(InspectionTaskSettings.CreateDefault(cameras, seedRecipe), cameras, seedRecipe, out triggerModesAdjusted);
        }
    }

    public static void Save(
        string path,
        InspectionTaskSettings settings,
        IReadOnlyList<InspectionCameraProfile>? cameras = null,
        InspectionRecipeEntry? seedRecipe = null)
    {
        var normalized = Normalize(settings, cameras, seedRecipe, out _);
        if (DbSession.IsInitialized)
        {
            SaveState(normalized, cameras, seedRecipe);
            return;
        }

        var json = JsonSerializer.Serialize(normalized, Options);
        File.WriteAllText(path, json);
    }

    private static InspectionTaskSettings LoadLegacyJson(
        string path,
        IReadOnlyList<InspectionCameraProfile>? cameras,
        InspectionRecipeEntry? seedRecipe,
        out bool triggerModesAdjusted)
    {
        try
        {
            if (!File.Exists(path))
            {
                return Normalize(InspectionTaskSettings.CreateDefault(cameras, seedRecipe), cameras, seedRecipe, out triggerModesAdjusted);
            }

            var json = File.ReadAllText(path);
            return Normalize(JsonSerializer.Deserialize<InspectionTaskSettings>(json, Options), cameras, seedRecipe, out triggerModesAdjusted);
        }
        catch
        {
            return Normalize(InspectionTaskSettings.CreateDefault(cameras, seedRecipe), cameras, seedRecipe, out triggerModesAdjusted);
        }
    }

    private static InspectionTaskSettings Normalize(
        InspectionTaskSettings? settings,
        IReadOnlyList<InspectionCameraProfile>? cameras,
        InspectionRecipeEntry? seedRecipe,
        out bool triggerModesAdjusted)
    {
        triggerModesAdjusted = false;
        settings ??= InspectionTaskSettings.CreateDefault(cameras, seedRecipe);
        settings.Definitions ??= [];
        settings.Instances ??= [];

        var definitions = settings.Definitions
            .Select((definition, index) => (definition ?? new InspectionTaskDefinition()).Normalize(index + 1))
            .ToList();

        if (definitions.Count == 0)
        {
            definitions.Add(InspectionTaskSettings.CreateDefault(cameras, seedRecipe).Definitions[0]);
        }

        var instances = settings.Instances
            .Select((instance, index) => (instance ?? new InspectionTaskInstance()).Normalize(index + 1, definitions))
            .ToList();

        if (instances.Count == 0)
        {
            instances.Add(InspectionTaskSettings.CreateDefault(cameras, seedRecipe).Instances[0].Normalize(1, definitions));
        }

        if (cameras is { Count: > 0 })
        {
            var cameraIds = cameras.Select(camera => camera.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var instance in instances)
            {
                instance.CameraIds = instance.CameraIds
                    .Where(cameraIds.Contains)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        triggerModesAdjusted = InspectionTaskTriggerCompatibility.NormalizeTriggerModes(
            new InspectionTaskSettings
            {
                Definitions = definitions,
                Instances = instances
            },
            cameras);

        return new InspectionTaskSettings
        {
            Definitions = definitions,
            Instances = instances
        };
    }

    private static string? LoadState()
    {
        return DbSession.ConfigDb.Queryable<CameraSettingsStateEntity>()
            .Where(entity => entity.Key == StateKey)
            .First()?.Value;
    }

    private static void SaveState(
        InspectionTaskSettings settings,
        IReadOnlyList<InspectionCameraProfile>? cameras,
        InspectionRecipeEntry? seedRecipe)
    {
        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var json = JsonSerializer.Serialize(Normalize(settings, cameras, seedRecipe, out _), Options);
        DbSession.ConfigDb.Storageable(new CameraSettingsStateEntity
        {
            Key = StateKey,
            Value = json,
            UpdatedUtcMs = nowUtcMs
        }).ExecuteCommand();
    }
}
