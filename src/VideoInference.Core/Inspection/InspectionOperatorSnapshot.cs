namespace VideoInferenceDemo;

public sealed record InspectionOperatorSnapshot(
    string EmployeeCode,
    string EmployeeName,
    string Team)
{
    public static InspectionOperatorSnapshot FromSession(PersonnelSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new InspectionOperatorSnapshot(session.EmployeeCode, session.EmployeeName, session.Team);
    }
}
