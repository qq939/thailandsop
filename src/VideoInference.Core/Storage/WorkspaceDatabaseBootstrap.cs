namespace VideoInferenceDemo;

public sealed record WorkspaceDatabasePaths(
    string LegacyDbPath,
    string ConfigDbPath,
    string ResultsDirectory);

public static class WorkspaceDatabaseBootstrap
{
    public static WorkspaceDatabasePaths Initialize(string baseDirectory)
    {
        var paths = ResolvePaths(baseDirectory);
        TryMigrateLegacyConfig(paths);
        DbSession.InitializeSplit(paths.ConfigDbPath, paths.ResultsDirectory);
        new PersonnelRepository(paths.ConfigDbPath).EnsureDefaultAdmin();
        return paths;
    }

    public static WorkspaceDatabasePaths ResolvePaths(string baseDirectory)
    {
        var root = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : baseDirectory;

        return new WorkspaceDatabasePaths(
            Path.Combine(root, "inference.db"),
            Path.Combine(root, "workspace_config.db"),
            Path.Combine(root, "results"));
    }

    private static void TryMigrateLegacyConfig(WorkspaceDatabasePaths paths)
    {
        try
        {
            _ = SqliteWorkspaceMigrator.TryMigrateLegacyConfig(paths.LegacyDbPath, paths.ConfigDbPath);
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Error("config", $"Failed to migrate legacy inference.db config: {ex.Message}");
        }
    }
}
