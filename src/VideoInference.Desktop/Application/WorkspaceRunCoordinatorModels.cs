namespace VideoInferenceDemo;

public sealed record PersonnelOptionsLoadResult(
    IReadOnlyList<PersonnelOptionItem> Options,
    PersonnelOptionItem? SelectedPersonnel);

public sealed record PersonnelSelectionStatus(
    bool IsSelected,
    string StatusText,
    string InferenceStatus,
    string LastError)
{
    public static PersonnelSelectionStatus Success { get; } = new(true, string.Empty, string.Empty, string.Empty);
}

public sealed record SessionRunBindingInfo(
    string RunUuid,
    string EmployeeCode,
    string EmployeeName);
