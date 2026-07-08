using SqlSugar;

namespace VideoInferenceDemo;

[SugarTable("fingerprint_modules")]
public sealed class FingerprintModuleEntity
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public string Id { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "enabled")]
    public int Enabled { get; set; }

    [SugarColumn(ColumnName = "connection_kind")]
    public string ConnectionKind { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "slave_address")]
    public int SlaveAddress { get; set; }

    [SugarColumn(ColumnName = "port_name")]
    public string PortName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "baud_rate")]
    public int BaudRate { get; set; }

    [SugarColumn(ColumnName = "data_bits")]
    public int DataBits { get; set; }

    [SugarColumn(ColumnName = "parity")]
    public string Parity { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "stop_bits")]
    public int StopBits { get; set; }

    [SugarColumn(ColumnName = "host")]
    public string Host { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "tcp_port")]
    public int TcpPort { get; set; }

    [SugarColumn(ColumnName = "read_timeout_ms")]
    public int ReadTimeoutMs { get; set; }

    [SugarColumn(ColumnName = "write_timeout_ms")]
    public int WriteTimeoutMs { get; set; }

    [SugarColumn(ColumnName = "poll_interval_ms")]
    public int PollIntervalMs { get; set; }

    [SugarColumn(ColumnName = "duplicate_suppress_ms")]
    public int DuplicateSuppressMs { get; set; }

    [SugarColumn(ColumnName = "sort_order")]
    public int SortOrder { get; set; }

    [SugarColumn(ColumnName = "created_utc_ms")]
    public long CreatedUtcMs { get; set; }

    [SugarColumn(ColumnName = "updated_utc_ms")]
    public long UpdatedUtcMs { get; set; }
}
