using VideoInferenceDemo;
using VideoInferenceDemo.ImageInspection.Runtime;

namespace VideoInferenceDemo.ImageInspection;

public sealed class InspectionParameterSettings
{
    public string DatabaseProvider { get; set; } = "SQLite";

    public string ConnectionString { get; set; } = "Server=127.0.0.1;Port=3306;Database=image_inspection;Uid=root;Pwd=;Connection Timeout=3;";

    public bool SaveSnapshots { get; set; } = true;

    public string SnapshotDirectory { get; set; } = "InspectionSnapshots";

    public string ProtocolType { get; set; } = "TCP";

    public string ProtocolEndpoint { get; set; } = "127.0.0.1:9000";

    public bool PublishResults { get; set; } = false;

    public int RetentionDays { get; set; } = 90;

    public bool EnableAutoCleanup { get; set; } = true;

    public InspectionPlcTriggerOptions PlcTrigger { get; set; } = new();

    public InspectionParameterSettings Normalize()
    {
        var provider = NormalizeProvider(DatabaseProvider);
        var connectionString = string.IsNullOrWhiteSpace(ConnectionString)
            ? "Server=127.0.0.1;Port=3306;Database=image_inspection;Uid=root;Pwd=;Connection Timeout=3;"
            : ConnectionString.Trim();
        if (string.Equals(provider, "MySQL", StringComparison.OrdinalIgnoreCase) &&
            connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            connectionString = "Server=127.0.0.1;Port=3306;Database=image_inspection;Uid=root;Pwd=;Connection Timeout=3;";
        }

        return new InspectionParameterSettings
        {
            DatabaseProvider = provider,
            ConnectionString = connectionString,
            SaveSnapshots = SaveSnapshots,
            SnapshotDirectory = string.IsNullOrWhiteSpace(SnapshotDirectory) ? "InspectionSnapshots" : SnapshotDirectory.Trim(),
            ProtocolType = string.IsNullOrWhiteSpace(ProtocolType) ? "TCP" : ProtocolType.Trim(),
            ProtocolEndpoint = string.IsNullOrWhiteSpace(ProtocolEndpoint) ? "127.0.0.1:9000" : ProtocolEndpoint.Trim(),
            PublishResults = PublishResults,
            RetentionDays = Math.Clamp(RetentionDays <= 0 ? 90 : RetentionDays, 1, 3650),
            EnableAutoCleanup = EnableAutoCleanup,
            PlcTrigger = (PlcTrigger ?? new InspectionPlcTriggerOptions()).Normalize()
        };
    }

    public static InspectionParameterSettings CreateDefault()
    {
        return new InspectionParameterSettings();
    }

    public InspectionResultStorageOptions ToResultStorageOptions()
    {
        var normalized = Normalize();
        var mode = string.Equals(normalized.DatabaseProvider, "MySQL", StringComparison.OrdinalIgnoreCase)
            ? InspectionResultStorageMode.MySqlPreferredWithSqliteFallback
            : InspectionResultStorageMode.SQLiteDaily;
        return new InspectionResultStorageOptions(mode, normalized.ConnectionString);
    }

    private static string NormalizeProvider(string? provider)
    {
        if (string.Equals(provider, "MySQL", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(provider, "MySqlPreferredWithSqliteFallback", StringComparison.OrdinalIgnoreCase))
        {
            return "MySQL";
        }

        return "SQLite";
    }
}
