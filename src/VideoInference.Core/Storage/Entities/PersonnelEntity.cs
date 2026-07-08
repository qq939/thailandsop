using SqlSugar;

namespace VideoInferenceDemo;

[SugarTable("personnel")]
public class PersonnelEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [SugarColumn(ColumnName = "employee_code")]
    public string EmployeeCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "employee_name")]
    public string EmployeeName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "password_text")]
    public string PasswordText { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "team", IsNullable = true)]
    public string? Team { get; set; }

    [SugarColumn(ColumnName = "fingerprint_id", IsNullable = true)]
    public int? FingerprintId { get; set; }

    [SugarColumn(ColumnName = "is_active")]
    public bool IsActive { get; set; } = true;

    [SugarColumn(ColumnName = "note", IsNullable = true)]
    public string? Note { get; set; }

    [SugarColumn(ColumnName = "created_utc_ms")]
    public long CreatedUtcMs { get; set; }

    [SugarColumn(ColumnName = "updated_utc_ms")]
    public long UpdatedUtcMs { get; set; }
}
