using SqlSugar;

namespace VideoInferenceDemo;

[SugarTable("run_operator_assignments")]
public class RunOperatorAssignmentEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "run_uuid")]
    public string RunUuid { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "employee_code")]
    public string EmployeeCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "employee_name", IsNullable = true)]
    public string? EmployeeName { get; set; }

    [SugarColumn(ColumnName = "employee_team", IsNullable = true)]
    public string? EmployeeTeam { get; set; }

    [SugarColumn(ColumnName = "session_name", IsNullable = true)]
    public string? SessionName { get; set; }

    [SugarColumn(ColumnName = "camera_id", IsNullable = true)]
    public string? CameraId { get; set; }

    [SugarColumn(ColumnName = "assigned_utc_ms")]
    public long AssignedUtcMs { get; set; }

    [SugarColumn(ColumnName = "released_utc_ms", IsNullable = true)]
    public long? ReleasedUtcMs { get; set; }

    [SugarColumn(ColumnName = "note", IsNullable = true)]
    public string? Note { get; set; }
}
