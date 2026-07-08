using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace VideoInferenceDemo;

public sealed class AppConfig
{
    public AppBrandingConfig Branding { get; set; } = new();
    public DbConfig Db { get; set; } = new();
    public AnalysisConfig Analysis { get; set; } = new();
    public List<FingerprintModuleOptions> FingerprintModules { get; set; } = new();
    public List<ModbusModuleOptions> ModbusModules { get; set; } = new();
    public List<SopProfile> SopProfiles { get; set; } = new();

    public static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            Branding = new AppBrandingConfig(),
            Db = new DbConfig(),
            Analysis = new AnalysisConfig(),
            FingerprintModules = new List<FingerprintModuleOptions>(),
            ModbusModules = new List<ModbusModuleOptions>(),
            SopProfiles = new List<SopProfile>()
        };
    }
}

public sealed class AppBrandingConfig
{
    public AppTitleConfig? VideoInference { get; set; }
    public AppTitleConfig? ImageInspection { get; set; }
    public string? WindowTitle { get; set; }
    public string? HomeTitle { get; set; }

    public AppTitleConfig Resolve(string appKey, string defaultWindowTitle, string defaultHomeTitle)
    {
        var scoped = string.Equals(appKey, AppBrandingKeys.ImageInspection, System.StringComparison.OrdinalIgnoreCase)
            ? ImageInspection
            : VideoInference;

        return new AppTitleConfig
        {
            WindowTitle = FirstNonBlank(scoped?.WindowTitle, WindowTitle, defaultWindowTitle),
            HomeTitle = FirstNonBlank(scoped?.HomeTitle, HomeTitle, defaultHomeTitle)
        };
    }

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }
}

public sealed class AppTitleConfig
{
    public string WindowTitle { get; set; } = string.Empty;
    public string HomeTitle { get; set; } = string.Empty;
}

public static class AppBrandingKeys
{
    public const string VideoInference = "VideoInference";
    public const string ImageInspection = "ImageInspection";
}

public static class AppConfigStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static AppConfig Load(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, "app_config.json");
        if (!File.Exists(path))
        {
            return AppConfig.CreateDefault();
        }

        try
        {
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), JsonOptions)
                   ?? AppConfig.CreateDefault();
        }
        catch
        {
            return AppConfig.CreateDefault();
        }
    }

    public static AppTitleConfig LoadBranding(
        string baseDirectory,
        string appKey,
        string defaultWindowTitle,
        string defaultHomeTitle)
    {
        return (Load(baseDirectory).Branding ?? new AppBrandingConfig())
            .Resolve(appKey, defaultWindowTitle, defaultHomeTitle);
    }
}
