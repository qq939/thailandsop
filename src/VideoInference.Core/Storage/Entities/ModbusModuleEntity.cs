using SqlSugar;

namespace VideoInferenceDemo;

[SugarTable("modbus_modules")]
public sealed class ModbusModuleEntity
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public string Id { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "enabled")]
    public int Enabled { get; set; }

    [SugarColumn(ColumnName = "host")]
    public string Host { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "port")]
    public int Port { get; set; }

    [SugarColumn(ColumnName = "slave_address")]
    public int SlaveAddress { get; set; }

    [SugarColumn(ColumnName = "poll_interval_ms")]
    public int PollIntervalMs { get; set; }

    [SugarColumn(ColumnName = "output_start_address")]
    public int OutputStartAddress { get; set; }

    [SugarColumn(ColumnName = "input_start_address")]
    public int InputStartAddress { get; set; }

    [SugarColumn(ColumnName = "sort_order")]
    public int SortOrder { get; set; }

    [SugarColumn(ColumnName = "created_utc_ms")]
    public long CreatedUtcMs { get; set; }

    [SugarColumn(ColumnName = "updated_utc_ms")]
    public long UpdatedUtcMs { get; set; }
}
