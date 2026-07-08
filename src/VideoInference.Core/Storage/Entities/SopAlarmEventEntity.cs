using SqlSugar;

namespace VideoInferenceDemo;

[SugarTable("sop_alarm_events")]
public class SopAlarmEventEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "event_uuid")]
    public string EventUuid { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "event_type")]
    public string EventType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "run_uuid", IsNullable = true)]
    public string? RunUuid { get; set; }

    [SugarColumn(ColumnName = "camera_id", IsNullable = true)]
    public string? CameraId { get; set; }

    [SugarColumn(ColumnName = "session_name", IsNullable = true)]
    public string? SessionName { get; set; }

    [SugarColumn(ColumnName = "source_key", IsNullable = true)]
    public string? SourceKey { get; set; }

    [SugarColumn(ColumnName = "step", IsNullable = true)]
    public int? Step { get; set; }

    [SugarColumn(ColumnName = "ng_reason", IsNullable = true)]
    public string? NgReason { get; set; }

    [SugarColumn(ColumnName = "reset_source", IsNullable = true)]
    public string? ResetSource { get; set; }

    [SugarColumn(ColumnName = "fingerprint_module_id", IsNullable = true)]
    public string? FingerprintModuleId { get; set; }

    [SugarColumn(ColumnName = "fingerprint_module_name", IsNullable = true)]
    public string? FingerprintModuleName { get; set; }

    [SugarColumn(ColumnName = "fingerprint_id", IsNullable = true)]
    public int? FingerprintId { get; set; }

    [SugarColumn(ColumnName = "employee_code", IsNullable = true)]
    public string? EmployeeCode { get; set; }

    [SugarColumn(ColumnName = "employee_name", IsNullable = true)]
    public string? EmployeeName { get; set; }

    [SugarColumn(ColumnName = "event_utc_ms")]
    public long EventUtcMs { get; set; }

    [SugarColumn(ColumnName = "related_alarm_event_uuid", IsNullable = true)]
    public string? RelatedAlarmEventUuid { get; set; }

    [SugarColumn(ColumnName = "note", IsNullable = true)]
    public string? Note { get; set; }
}
