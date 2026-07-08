namespace VideoInferenceDemo;

public static class SopAlarmEventTypes
{
    public const string Alarm = "alarm";
    public const string Reset = "reset";
}

public sealed record SopAlarmEventRecord(
    string EventUuid,
    string EventType,
    string RunUuid,
    string CameraId,
    string SessionName,
    string SourceKey,
    int? Step,
    string NgReason,
    string ResetSource,
    string FingerprintModuleId,
    string FingerprintModuleName,
    int? FingerprintId,
    string EmployeeCode,
    string EmployeeName,
    long EventUtcMs,
    string RelatedAlarmEventUuid,
    string Note);

public sealed class SopAlarmEventRepository
{
    public SopAlarmEventRepository(string dbPath)
    {
    }

    public void Insert(SopAlarmEventRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrWhiteSpace(record.EventUuid))
            throw new ArgumentException("Event uuid is required.", nameof(record));

        if (record.EventType is not SopAlarmEventTypes.Alarm and not SopAlarmEventTypes.Reset)
            throw new ArgumentOutOfRangeException(nameof(record), record.EventType, "Unsupported SOP alarm event type.");

        DbSession.ResultDb.Insertable(new SopAlarmEventEntity
        {
            EventUuid = record.EventUuid.Trim(),
            EventType = record.EventType.Trim(),
            RunUuid = string.IsNullOrWhiteSpace(record.RunUuid) ? null : record.RunUuid.Trim(),
            CameraId = string.IsNullOrWhiteSpace(record.CameraId) ? null : record.CameraId.Trim(),
            SessionName = string.IsNullOrWhiteSpace(record.SessionName) ? null : record.SessionName.Trim(),
            SourceKey = string.IsNullOrWhiteSpace(record.SourceKey) ? null : record.SourceKey.Trim(),
            Step = record.Step,
            NgReason = string.IsNullOrWhiteSpace(record.NgReason) ? null : record.NgReason.Trim(),
            ResetSource = string.IsNullOrWhiteSpace(record.ResetSource) ? null : record.ResetSource.Trim(),
            FingerprintModuleId = string.IsNullOrWhiteSpace(record.FingerprintModuleId) ? null : record.FingerprintModuleId.Trim(),
            FingerprintModuleName = string.IsNullOrWhiteSpace(record.FingerprintModuleName) ? null : record.FingerprintModuleName.Trim(),
            FingerprintId = record.FingerprintId,
            EmployeeCode = string.IsNullOrWhiteSpace(record.EmployeeCode) ? null : record.EmployeeCode.Trim(),
            EmployeeName = string.IsNullOrWhiteSpace(record.EmployeeName) ? null : record.EmployeeName.Trim(),
            EventUtcMs = record.EventUtcMs > 0 ? record.EventUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            RelatedAlarmEventUuid = string.IsNullOrWhiteSpace(record.RelatedAlarmEventUuid) ? null : record.RelatedAlarmEventUuid.Trim(),
            Note = string.IsNullOrWhiteSpace(record.Note) ? null : record.Note.Trim()
        }).ExecuteCommand();
    }

    public IReadOnlyList<SopAlarmEventRecord> ListByRunUuid(string runUuid)
    {
        if (string.IsNullOrWhiteSpace(runUuid))
            return Array.Empty<SopAlarmEventRecord>();

        return DbSession.ResultDb.Queryable<SopAlarmEventEntity>()
            .Where(e => e.RunUuid == runUuid.Trim())
            .OrderBy(e => e.EventUtcMs)
            .OrderBy(e => e.Id)
            .ToList()
            .Select(e => new SopAlarmEventRecord(
                e.EventUuid,
                e.EventType,
                e.RunUuid ?? string.Empty,
                e.CameraId ?? string.Empty,
                e.SessionName ?? string.Empty,
                e.SourceKey ?? string.Empty,
                e.Step,
                e.NgReason ?? string.Empty,
                e.ResetSource ?? string.Empty,
                e.FingerprintModuleId ?? string.Empty,
                e.FingerprintModuleName ?? string.Empty,
                e.FingerprintId,
                e.EmployeeCode ?? string.Empty,
                e.EmployeeName ?? string.Empty,
                e.EventUtcMs,
                e.RelatedAlarmEventUuid ?? string.Empty,
                e.Note ?? string.Empty))
            .ToList();
    }
}
