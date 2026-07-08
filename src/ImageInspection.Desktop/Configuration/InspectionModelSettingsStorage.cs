using System.IO;
using System.Text.Json;

namespace VideoInferenceDemo.ImageInspection;

public static class InspectionModelSettingsStorage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static InspectionModelSettings Load(string path, string? baseDirectory = null)
    {
        try
        {
            if (!File.Exists(path))
            {
                return NormalizeWithFallback(
                    InspectionModelSettings.CreateDefault(ResolveBaseDirectory(baseDirectory)),
                    ResolveBaseDirectory(baseDirectory));
            }

            var json = File.ReadAllText(path);
            return NormalizeWithDiscoveredModels(
                JsonSerializer.Deserialize<InspectionModelSettings>(json, Options),
                ResolveBaseDirectory(baseDirectory));
        }
        catch
        {
            return NormalizeWithFallback(
                InspectionModelSettings.CreateDefault(ResolveBaseDirectory(baseDirectory)),
                ResolveBaseDirectory(baseDirectory));
        }
    }

    public static void Save(string path, InspectionModelSettings settings)
    {
        var json = JsonSerializer.Serialize(Normalize(settings), Options);
        File.WriteAllText(path, json);
    }

    private static InspectionModelSettings Normalize(InspectionModelSettings? settings)
    {
        settings ??= new InspectionModelSettings();
        settings.Models ??= [];

        var models = settings.Models
            .Select((model, index) => (model ?? new InspectionModelConfig()).Normalize(index + 1))
            .ToList();

        return new InspectionModelSettings
        {
            Models = models
        };
    }

    private static InspectionModelSettings NormalizeWithDiscoveredModels(
        InspectionModelSettings? settings,
        string baseDirectory)
    {
        var normalized = Normalize(settings);
        var discovered = InspectionModelSettings.DiscoverDlModels(baseDirectory);
        return NormalizeWithFallback(
            InspectionModelSettings.MergeDiscoveredModels(normalized, discovered),
            baseDirectory);
    }

    private static InspectionModelSettings NormalizeWithFallback(
        InspectionModelSettings? settings,
        string baseDirectory)
    {
        var normalized = Normalize(settings);
        if (normalized.Models.Count > 0)
        {
            return normalized;
        }

        var fallback = InspectionModelSettings.CreateDefault(baseDirectory);
        return Normalize(fallback.Models.Count > 0
            ? fallback
            : InspectionModelSettings.CreateDefault(AppContext.BaseDirectory));
    }

    private static string ResolveBaseDirectory(string? baseDirectory)
    {
        return string.IsNullOrWhiteSpace(baseDirectory) ? AppContext.BaseDirectory : baseDirectory;
    }
}
