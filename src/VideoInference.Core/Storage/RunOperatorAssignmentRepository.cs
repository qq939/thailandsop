namespace VideoInferenceDemo;

public sealed record RunOperatorAssignmentRecord(
    string RunUuid,
    string EmployeeCode,
    string EmployeeName,
    string EmployeeTeam,
    string SessionName,
    string CameraId,
    long AssignedUtcMs,
    long? ReleasedUtcMs,
    string Note);

public sealed class CameraOptionRecord
{
    public string CameraId { get; set; } = string.Empty;
    public string SessionName { get; set; } = string.Empty;
}

public sealed class RunOperatorAssignmentRepository
{
    public RunOperatorAssignmentRepository(string dbPath)
    {
    }

    public void Upsert(
        string runUuid,
        string employeeCode,
        string? sessionName,
        string? cameraId,
        long assignedUtcMs,
        long? releasedUtcMs = null,
        string? note = null)
    {
        Upsert(
            runUuid,
            employeeCode,
            employeeName: null,
            employeeTeam: null,
            sessionName,
            cameraId,
            assignedUtcMs,
            releasedUtcMs,
            note);
    }

    public void Upsert(
        string runUuid,
        string employeeCode,
        string? employeeName,
        string? employeeTeam,
        string? sessionName,
        string? cameraId,
        long assignedUtcMs,
        long? releasedUtcMs = null,
        string? note = null)
    {
        if (string.IsNullOrWhiteSpace(runUuid))
            throw new ArgumentException("Run uuid is required.", nameof(runUuid));
        if (string.IsNullOrWhiteSpace(employeeCode))
            throw new ArgumentException("Employee code is required.", nameof(employeeCode));

        var assigned = assignedUtcMs > 0
            ? assignedUtcMs
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        DbSession.ResultDb.Ado.ExecuteCommand(@"
INSERT INTO run_operator_assignments (
  run_uuid, employee_code, employee_name, employee_team, session_name, camera_id,
  assigned_utc_ms, released_utc_ms, note)
VALUES (
  @run_uuid, @employee_code, @employee_name, @employee_team, @session_name, @camera_id,
  @assigned_utc_ms, @released_utc_ms, @note)
ON CONFLICT(run_uuid) DO UPDATE SET
  employee_code = excluded.employee_code,
  employee_name = COALESCE(excluded.employee_name, run_operator_assignments.employee_name),
  employee_team = COALESCE(excluded.employee_team, run_operator_assignments.employee_team),
  session_name = COALESCE(excluded.session_name, run_operator_assignments.session_name),
  camera_id = COALESCE(excluded.camera_id, run_operator_assignments.camera_id),
  assigned_utc_ms = CASE
    WHEN run_operator_assignments.assigned_utc_ms <= excluded.assigned_utc_ms THEN run_operator_assignments.assigned_utc_ms
    ELSE excluded.assigned_utc_ms
  END,
  released_utc_ms = CASE
    WHEN excluded.released_utc_ms IS NULL THEN run_operator_assignments.released_utc_ms
    WHEN run_operator_assignments.released_utc_ms IS NULL THEN excluded.released_utc_ms
    WHEN run_operator_assignments.released_utc_ms < excluded.released_utc_ms THEN excluded.released_utc_ms
    ELSE run_operator_assignments.released_utc_ms
  END,
  note = COALESCE(excluded.note, run_operator_assignments.note);",
            new
            {
                run_uuid = runUuid.Trim(),
                employee_code = employeeCode.Trim(),
                employee_name = string.IsNullOrWhiteSpace(employeeName) ? null : employeeName.Trim(),
                employee_team = string.IsNullOrWhiteSpace(employeeTeam) ? null : employeeTeam.Trim(),
                session_name = string.IsNullOrWhiteSpace(sessionName) ? null : sessionName.Trim(),
                camera_id = string.IsNullOrWhiteSpace(cameraId) ? null : cameraId.Trim(),
                assigned_utc_ms = assigned,
                released_utc_ms = releasedUtcMs,
                note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
            });
    }

    public bool MarkReleased(string runUuid, long? releasedUtcMs = null)
    {
        if (string.IsNullOrWhiteSpace(runUuid))
            return false;

        var released = releasedUtcMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var count = DbSession.ResultDb.Ado.ExecuteCommand(@"
UPDATE run_operator_assignments
SET released_utc_ms = CASE
  WHEN released_utc_ms IS NULL THEN @released_utc_ms
  WHEN released_utc_ms < @released_utc_ms THEN @released_utc_ms
  ELSE released_utc_ms
END
WHERE run_uuid = @run_uuid;",
            new { released_utc_ms = released, run_uuid = runUuid.Trim() });
        return count > 0;
    }

    public RunOperatorAssignmentRecord? GetByRunUuid(string runUuid)
    {
        if (string.IsNullOrWhiteSpace(runUuid))
            return null;

        var entity = DbSession.ResultDb.Queryable<RunOperatorAssignmentEntity>()
            .Where(e => e.RunUuid == runUuid.Trim())
            .First();

        if (entity == null)
            return null;

        return new RunOperatorAssignmentRecord(
            entity.RunUuid,
            entity.EmployeeCode,
            entity.EmployeeName ?? string.Empty,
            entity.EmployeeTeam ?? string.Empty,
            entity.SessionName ?? string.Empty,
            entity.CameraId ?? string.Empty,
            entity.AssignedUtcMs,
            entity.ReleasedUtcMs,
            entity.Note ?? string.Empty);
    }

    public IReadOnlyList<CameraOptionRecord> ListCameraOptions()
    {
        return DbSession.ResultDb.SqlQueryable<CameraOptionRecord>(@"
SELECT
  camera_id,
  COALESCE(
    MAX(NULLIF(TRIM(session_name), '')),
    camera_id
  ) AS session_name
FROM run_operator_assignments
WHERE TRIM(COALESCE(camera_id, '')) <> ''
GROUP BY camera_id
ORDER BY session_name, camera_id;")
        .ToList();
    }
}
