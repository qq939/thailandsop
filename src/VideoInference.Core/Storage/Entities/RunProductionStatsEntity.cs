using SqlSugar;

namespace VideoInferenceDemo;

[SugarTable("run_production_stats")]
public class RunProductionStatsEntity
{
    [SugarColumn(ColumnName = "run_uuid", IsPrimaryKey = true)]
    public string RunUuid { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "ok_count")]
    public long OkCount { get; set; }

    [SugarColumn(ColumnName = "ng_count")]
    public long NgCount { get; set; }

    [SugarColumn(ColumnName = "updated_utc_ms")]
    public long UpdatedUtcMs { get; set; }
}
