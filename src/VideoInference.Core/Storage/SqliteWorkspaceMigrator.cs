using SqlSugar;

namespace VideoInferenceDemo;

public static class SqliteWorkspaceMigrator
{
    private static readonly string[] ConfigTables =
    [
        "personnel",
        "personnel_fingerprint_bindings",
        "camera_profiles",
        "camera_settings_state",
        "sop_profiles",
        "sop_steps",
        "fingerprint_modules",
        "modbus_modules",
        "three_color_light_bindings"
    ];

    public static bool TryMigrateLegacyConfig(string legacyDbPath, string configDbPath)
    {
        if (string.IsNullOrWhiteSpace(legacyDbPath) ||
            string.IsNullOrWhiteSpace(configDbPath) ||
            !File.Exists(legacyDbPath) ||
            File.Exists(configDbPath))
        {
            return false;
        }

        var legacy = DbSession.CreateScope(legacyDbPath, _ => { }, foreignKeys: false);
        var config = DbSession.CreateScope(configDbPath, SqliteConfigSchema.Ensure, foreignKeys: true);
        try
        {
            var alias = $"legacy_{Guid.NewGuid():N}";
            var quotedAlias = QuoteIdentifier(alias);
            var attachPath = Path.GetFullPath(legacyDbPath).Replace("'", "''", StringComparison.Ordinal);
            config.Ado.ExecuteCommand($"ATTACH DATABASE '{attachPath}' AS {quotedAlias};");
            try
            {
                foreach (var tableName in ConfigTables)
                {
                    CopyTableIfExists(legacy, config, quotedAlias, tableName);
                }
            }
            finally
            {
                config.Ado.ExecuteCommand($"DETACH DATABASE {quotedAlias};");
            }

            return true;
        }
        finally
        {
            legacy.Dispose();
            config.Dispose();
        }
    }

    private static void CopyTableIfExists(ISqlSugarClient legacy, ISqlSugarClient config, string quotedLegacyAlias, string tableName)
    {
        if (!TableExists(legacy, tableName))
        {
            return;
        }

        var columns = legacy.Ado.SqlQuery<string>(
                $"SELECT name FROM pragma_table_info('{tableName}')")
            .Where(column => ColumnExists(config, tableName, column))
            .ToArray();
        if (columns.Length == 0)
        {
            return;
        }

        var columnList = string.Join(", ", columns.Select(QuoteIdentifier));
        var quotedTable = QuoteIdentifier(tableName);
        config.Ado.ExecuteCommand(
            $"INSERT OR IGNORE INTO {quotedTable} ({columnList}) SELECT {columnList} FROM {quotedLegacyAlias}.{quotedTable};");
    }

    private static bool TableExists(ISqlSugarClient db, string tableName)
    {
        var count = db.Ado.SqlQuery<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;",
            new { name = tableName }).FirstOrDefault();
        return count > 0;
    }

    private static bool ColumnExists(ISqlSugarClient db, string tableName, string columnName)
    {
        return db.Ado.SqlQuery<string>(
            $"SELECT name FROM pragma_table_info('{tableName}') WHERE name = @name;",
            new { name = columnName }).Count > 0;
    }

    private static string QuoteIdentifier(string value)
    {
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

}
