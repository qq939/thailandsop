using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VideoInferenceDemo;

public static class ModelBindingPlanFactory
{
    public static ModelBindingPlan Create(ModelCatalogEntry model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var classConfig = LoadClassConfig(model.ClassConfigPath);
        return new ModelBindingPlan(
            model,
            classConfig.ClassNames,
            classConfig.BoxColor,
            classConfig.BoxColors,
            classConfig.BoxThickness,
            classConfig.LabelFontScale);
    }

    private static ClassConfig LoadClassConfig(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ClassConfig.Empty;
        }

        var resolved = ResolvePath(path);
        if (!File.Exists(resolved))
        {
            return ClassConfig.Empty;
        }

        var ext = Path.GetExtension(resolved).ToLowerInvariant();
        if (ext == ".json")
        {
            var json = File.ReadAllText(resolved);
            return TryParseClassConfigJson(json, out var config)
                ? config
                : ClassConfig.Empty;
        }

        var lines = File.ReadAllLines(resolved);
        return new ClassConfig(
            lines.Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToArray(),
            null,
            null,
            null,
            null);
    }

    private static string ResolvePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
    }

    private static bool TryParseClassConfigJson(string json, out ClassConfig config)
    {
        config = ClassConfig.Empty;

        try
        {
            var legacyClasses = JsonSerializer.Deserialize<string[]>(json);
            if (legacyClasses != null)
            {
                config = new ClassConfig(legacyClasses, null, null, null, null);
                return true;
            }
        }
        catch (JsonException)
        {
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var fileConfig = JsonSerializer.Deserialize<ClassConfigFile>(json, options);
            if (fileConfig == null)
            {
                return false;
            }

            var classNames = fileConfig.Classes ?? fileConfig.ClassNames;
            var boxColor = ResolveSingleColor(fileConfig.BoxColor, fileConfig.Color);
            var boxColors = ResolveColorList(fileConfig);
            var boxThickness = fileConfig.BoxThickness ?? fileConfig.Thickness;
            var labelFontScale = fileConfig.LabelFontScale ?? fileConfig.FontScale;

            config = new ClassConfig(classNames, boxColor, boxColors, boxThickness, labelFontScale);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ResolveSingleColor(JsonElement primary, JsonElement fallback)
    {
        var color = GetJsonString(primary);
        return string.IsNullOrWhiteSpace(color) ? GetJsonString(fallback) : color;
    }

    private static string[]? ResolveColorList(ClassConfigFile fileConfig)
    {
        if (fileConfig.BoxColors is { Length: > 0 })
        {
            return fileConfig.BoxColors;
        }

        if (fileConfig.Colors is { Length: > 0 })
        {
            return fileConfig.Colors;
        }

        var fromBoxColor = GetJsonStringArray(fileConfig.BoxColor);
        if (fromBoxColor is { Length: > 0 })
        {
            return fromBoxColor;
        }

        return GetJsonStringArray(fileConfig.Color);
    }

    private static string? GetJsonString(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static string[]? GetJsonStringArray(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = value
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim())
            .ToArray();
        return values.Length == 0 ? null : values;
    }

    private sealed record ClassConfig(
        string[]? ClassNames,
        string? BoxColor,
        string[]? BoxColors,
        int? BoxThickness,
        double? LabelFontScale)
    {
        public static ClassConfig Empty { get; } = new(null, null, null, null, null);
    }

    private sealed class ClassConfigFile
    {
        public string[]? Classes { get; set; }
        public string[]? ClassNames { get; set; }
        public JsonElement BoxColor { get; set; }
        public JsonElement Color { get; set; }
        public string[]? BoxColors { get; set; }
        public string[]? Colors { get; set; }
        public int? BoxThickness { get; set; }
        public int? Thickness { get; set; }
        public double? LabelFontScale { get; set; }
        public double? FontScale { get; set; }
    }
}
