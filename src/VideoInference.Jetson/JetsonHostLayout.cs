using System.IO;

namespace VideoInferenceDemo;

internal sealed record JetsonHostLayout(
    string BaseDirectory,
    string CameraConfigPath,
    string ModelRootPath,
    string LogDirectory)
{
    public string DiagnosticsReportPath => Path.Combine(LogDirectory, "environment_diagnostics.log");

    public static JetsonHostLayout Resolve(string baseDirectory, string? cameraConfigPath, string? modelRootPath, string? logDirectory)
    {
        var resolvedBase = string.IsNullOrWhiteSpace(baseDirectory) ? AppContext.BaseDirectory : baseDirectory;
        var resolvedCameraConfig = ResolvePath(resolvedBase, cameraConfigPath, "camera_config.json");
        var resolvedModelRoot = ResolvePath(resolvedBase, modelRootPath, "DL");
        var resolvedLogDirectory = ResolvePath(resolvedBase, logDirectory, "logs");

        return new JetsonHostLayout(
            resolvedBase,
            resolvedCameraConfig,
            resolvedModelRoot,
            resolvedLogDirectory);
    }

    private static string ResolvePath(string baseDirectory, string? overridePath, string fallbackRelativePath)
    {
        if (string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.Combine(baseDirectory, fallbackRelativePath);
        }

        return Path.IsPathRooted(overridePath)
            ? overridePath
            : Path.Combine(baseDirectory, overridePath);
    }
}
