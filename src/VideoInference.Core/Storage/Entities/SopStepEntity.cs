using SqlSugar;

namespace VideoInferenceDemo;

[SugarTable("sop_steps")]
public sealed class SopStepEntity
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public int Id { get; set; }

    [SugarColumn(ColumnName = "profile_id")]
    public string ProfileId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "step")]
    public int Step { get; set; }

    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "action_code")]
    public string? ActionCode { get; set; }

    [SugarColumn(ColumnName = "tcn_label")]
    public string? TcnLabel { get; set; }

    [SugarColumn(ColumnName = "expected_state_code")]
    public string? ExpectedStateCode { get; set; }

    [SugarColumn(ColumnName = "created_utc_ms")]
    public long CreatedUtcMs { get; set; }

    [SugarColumn(ColumnName = "updated_utc_ms")]
    public long UpdatedUtcMs { get; set; }
}
