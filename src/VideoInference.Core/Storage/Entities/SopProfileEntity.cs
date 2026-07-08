using SqlSugar;

namespace VideoInferenceDemo;

[SugarTable("sop_profiles")]
public sealed class SopProfileEntity
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public string Id { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "strategy")]
    public string Strategy { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "fingerprint_module_id")]
    public string FingerprintModuleId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "sort_order")]
    public int SortOrder { get; set; }

    [SugarColumn(ColumnName = "created_utc_ms")]
    public long CreatedUtcMs { get; set; }

    [SugarColumn(ColumnName = "updated_utc_ms")]
    public long UpdatedUtcMs { get; set; }
}
