namespace VideoInferenceDemo;

public sealed record SopFaultAlarmEvent(
    CameraSessionViewModel Session,
    string EventUuid,
    string RunUuid,
    string SourceKey,
    int? Step,
    string NgReason,
    long EventUtcMs);

public sealed record SopFaultResetContext(
    string ResetSource,
    string? FingerprintModuleId = null,
    string? FingerprintModuleName = null,
    int? FingerprintId = null,
    string? EmployeeCode = null,
    string? EmployeeName = null,
    string? RelatedAlarmEventUuid = null,
    string? Note = null)
{
    public static SopFaultResetContext ManualButton { get; } = new("manual_button");
}

public sealed record SopFaultResetEvent(
    CameraSessionViewModel Session,
    string EventUuid,
    string RunUuid,
    string SourceKey,
    int? Step,
    string NgReason,
    long EventUtcMs,
    SopFaultResetContext Context);
