using System.IO;
using System.Text.Json;
using VideoInferenceDemo;

namespace VideoInferenceDemo.ImageInspection;

public static class InspectionRecipeCatalogStorage
{
    private const string StateKey = "image_inspection_recipe_catalog";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static InspectionRecipeCatalog Load(string path)
    {
        try
        {
            if (DbSession.IsInitialized)
            {
                var state = LoadState();
                if (!string.IsNullOrWhiteSpace(state))
                {
                    return Normalize(JsonSerializer.Deserialize<InspectionRecipeCatalog>(state, Options));
                }

                var legacy = LoadLegacyJson(path);
                SaveState(legacy);
                return legacy;
            }

            return LoadLegacyJson(path);
        }
        catch
        {
            return InspectionRecipeCatalog.CreateDefault();
        }
    }

    public static void Save(string path, InspectionRecipeCatalog catalog)
    {
        var normalized = Normalize(catalog);
        if (DbSession.IsInitialized)
        {
            SaveState(normalized);
            return;
        }

        var json = JsonSerializer.Serialize(normalized, Options);
        File.WriteAllText(path, json);
    }

    private static InspectionRecipeCatalog LoadLegacyJson(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return InspectionRecipeCatalog.CreateDefault();
            }

            var json = File.ReadAllText(path);
            return Normalize(JsonSerializer.Deserialize<InspectionRecipeCatalog>(json, Options));
        }
        catch
        {
            return InspectionRecipeCatalog.CreateDefault();
        }
    }

    private static InspectionRecipeCatalog Normalize(InspectionRecipeCatalog? catalog)
    {
        catalog ??= InspectionRecipeCatalog.CreateDefault();
        catalog.Recipes ??= [];
        foreach (var recipe in catalog.Recipes)
        {
            recipe.ReferenceImagePathsByCameraId ??= [];
            recipe.AlignmentByCameraId ??= [];
            recipe.Rois ??= [];
            recipe.ModelBindings ??= [];
            recipe.Parameters ??= [];
        }

        return catalog;
    }

    private static string? LoadState()
    {
        return DbSession.ConfigDb.Queryable<CameraSettingsStateEntity>()
            .Where(entity => entity.Key == StateKey)
            .First()?.Value;
    }

    private static void SaveState(InspectionRecipeCatalog catalog)
    {
        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var json = JsonSerializer.Serialize(Normalize(catalog), Options);
        DbSession.ConfigDb.Storageable(new CameraSettingsStateEntity
        {
            Key = StateKey,
            Value = json,
            UpdatedUtcMs = nowUtcMs
        }).ExecuteCommand();
    }
}
