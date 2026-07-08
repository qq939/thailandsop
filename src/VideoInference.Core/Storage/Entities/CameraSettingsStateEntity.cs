using SqlSugar;

namespace VideoInferenceDemo;

[SugarTable("camera_settings_state")]
public sealed class CameraSettingsStateEntity
{
    [SugarColumn(ColumnName = "key", IsPrimaryKey = true)]
    public string Key { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "value")]
    public string Value { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "updated_utc_ms")]
    public long UpdatedUtcMs { get; set; }
}
