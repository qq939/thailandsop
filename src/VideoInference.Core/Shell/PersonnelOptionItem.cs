namespace VideoInferenceDemo;

public sealed class PersonnelOptionItem
{
    public PersonnelOptionItem(string employeeCode, string employeeName, string? team = null)
    {
        EmployeeCode = employeeCode;
        EmployeeName = employeeName;
        Team = team ?? string.Empty;
    }

    public string EmployeeCode { get; }

    public string EmployeeName { get; }

    public string Team { get; }

    public string DisplayText => string.IsNullOrWhiteSpace(EmployeeName) || string.Equals(EmployeeName, EmployeeCode, StringComparison.OrdinalIgnoreCase)
        ? EmployeeCode
        : $"{EmployeeName} ({EmployeeCode})";
}
