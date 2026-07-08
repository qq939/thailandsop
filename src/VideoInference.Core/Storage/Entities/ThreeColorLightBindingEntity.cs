using SqlSugar;

namespace VideoInferenceDemo;

[SugarTable("three_color_light_bindings")]
public sealed class ThreeColorLightBindingEntity
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(ColumnName = "module_id")]
    public string ModuleId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "light_number")]
    public int LightNumber { get; set; }

    [SugarColumn(ColumnName = "red_channel_number")]
    public int RedChannelNumber { get; set; }

    [SugarColumn(ColumnName = "green_channel_number")]
    public int GreenChannelNumber { get; set; }

    [SugarColumn(ColumnName = "buzzer_channel_number")]
    public int BuzzerChannelNumber { get; set; }

    [SugarColumn(ColumnName = "buzzer_enabled")]
    public int BuzzerEnabled { get; set; }

    [SugarColumn(ColumnName = "sort_order")]
    public int SortOrder { get; set; }

    [SugarColumn(ColumnName = "created_utc_ms")]
    public long CreatedUtcMs { get; set; }

    [SugarColumn(ColumnName = "updated_utc_ms")]
    public long UpdatedUtcMs { get; set; }
}
