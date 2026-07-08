namespace VideoInferenceDemo;

public sealed record PersonnelRecord(
    string EmployeeCode,
    string EmployeeName,
    string Team,
    int? FingerprintId,
    bool IsActive,
    string Note,
    long CreatedUtcMs,
    long UpdatedUtcMs,
    string PasswordText = "")
{
    public bool IsAdmin => PersonnelRepository.IsAdminCode(EmployeeCode);
}

public sealed class PersonnelRepository
{
    public PersonnelRepository(string dbPath)
    {
    }

    public IReadOnlyList<PersonnelRecord> List(bool includeInactive = false)
    {
        return DbSession.ConfigDb.Queryable<PersonnelEntity>()
            .Where(e => includeInactive || e.IsActive)
            .OrderBy("is_active DESC, employee_code ASC")
            .ToList()
            .Select(e => new PersonnelRecord(
                e.EmployeeCode,
                e.EmployeeName,
                e.Team ?? string.Empty,
                e.FingerprintId,
                e.IsActive,
                e.Note ?? string.Empty,
                e.CreatedUtcMs,
                e.UpdatedUtcMs,
                e.PasswordText ?? string.Empty))
            .ToList();
    }

    public void EnsureDefaultAdmin()
    {
        var existing = GetByCode(AdminEmployeeCode);
        if (existing != null)
        {
            if (!existing.IsActive)
            {
                SetActive(AdminEmployeeCode, true);
            }

            return;
        }

        Upsert(
            AdminEmployeeCode,
            AdminEmployeeName,
            team: string.Empty,
            isActive: true,
            note: "System administrator",
            fingerprintId: null,
            passwordText: AdminDefaultPassword);
    }

    public void SetFingerprintBinding(string employeeCode, string fingerprintModuleId, int fingerprintId)
    {
        if (string.IsNullOrWhiteSpace(employeeCode))
            throw new ArgumentException("Employee code is required.", nameof(employeeCode));
        if (string.IsNullOrWhiteSpace(fingerprintModuleId))
            throw new ArgumentException("Fingerprint module ID is required.", nameof(fingerprintModuleId));
        if (fingerprintId is < 1 or > 255)
            throw new ArgumentOutOfRangeException(nameof(fingerprintId), fingerprintId, "Must be 1-255.");

        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var result = DbSession.ConfigDb.Ado.UseTran(() =>
        {
            DbSession.ConfigDb.Ado.ExecuteCommand(@"
INSERT INTO personnel_fingerprint_bindings (
  employee_code, fingerprint_module_id, fingerprint_id, created_utc_ms, updated_utc_ms)
VALUES (
  @emp_code, @module_id, @fp_id, @now, @now)
ON CONFLICT(employee_code, fingerprint_module_id) DO UPDATE SET
  fingerprint_id = excluded.fingerprint_id,
  updated_utc_ms = excluded.updated_utc_ms;",
                new { emp_code = employeeCode.Trim(), module_id = fingerprintModuleId.Trim(), fp_id = fingerprintId, now = nowUtcMs });

            DbSession.ConfigDb.Ado.ExecuteCommand(@"
UPDATE personnel
SET fingerprint_id = @fingerprint_id, updated_utc_ms = @now_utc_ms
WHERE employee_code = @employee_code;",
                new { fingerprint_id = fingerprintId, now_utc_ms = nowUtcMs, employee_code = employeeCode.Trim() });
        });

        if (!result.IsSuccess)
            throw new InvalidOperationException("Failed to set fingerprint binding.", result.ErrorException);
    }

    public int? GetFingerprintBinding(string employeeCode, string fingerprintModuleId)
    {
        if (string.IsNullOrWhiteSpace(employeeCode) || string.IsNullOrWhiteSpace(fingerprintModuleId))
            return null;

        return DbSession.ConfigDb.Ado.SqlQuery<int?>(@"
SELECT fingerprint_id
FROM personnel_fingerprint_bindings
WHERE employee_code = @emp_code AND fingerprint_module_id = @module_id;",
            new { emp_code = employeeCode.Trim(), module_id = fingerprintModuleId.Trim() })
            .FirstOrDefault();
    }

    public void NormalizeFingerprintBindingsForModules(IEnumerable<string> activeModuleIds)
    {
        var moduleIds = (activeModuleIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (moduleIds.Length != 1)
            return;

        var targetModuleId = moduleIds[0];
        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var result = DbSession.ConfigDb.Ado.UseTran(() =>
        {
            DbSession.ConfigDb.Ado.ExecuteCommand(@"
UPDATE personnel_fingerprint_bindings
SET fingerprint_module_id = @target_module_id, updated_utc_ms = @now_utc_ms
WHERE fingerprint_module_id <> @target_module_id
  AND NOT EXISTS (
    SELECT 1
    FROM personnel_fingerprint_bindings existing
    WHERE existing.fingerprint_module_id = @target_module_id
      AND (
        existing.employee_code = personnel_fingerprint_bindings.employee_code
        OR existing.fingerprint_id = personnel_fingerprint_bindings.fingerprint_id
      )
  );",
                new { target_module_id = targetModuleId, now_utc_ms = nowUtcMs });

            DbSession.ConfigDb.Ado.ExecuteCommand(@"
UPDATE personnel
SET fingerprint_id = (
    SELECT b.fingerprint_id
    FROM personnel_fingerprint_bindings b
    WHERE b.employee_code = personnel.employee_code
      AND b.fingerprint_module_id = @target_module_id
    LIMIT 1
  ),
  updated_utc_ms = @now_utc_ms
WHERE EXISTS (
  SELECT 1
  FROM personnel_fingerprint_bindings b
  WHERE b.employee_code = personnel.employee_code
    AND b.fingerprint_module_id = @target_module_id
);",
                new { target_module_id = targetModuleId, now_utc_ms = nowUtcMs });
        });

        if (!result.IsSuccess)
            throw new InvalidOperationException("Failed to normalize fingerprint bindings.", result.ErrorException);
    }

    public void RemoveFingerprintBinding(string employeeCode, string fingerprintModuleId)
    {
        if (string.IsNullOrWhiteSpace(employeeCode) || string.IsNullOrWhiteSpace(fingerprintModuleId))
            return;

        DbSession.ConfigDb.Ado.ExecuteCommand(@"
DELETE FROM personnel_fingerprint_bindings
WHERE employee_code = @emp_code AND fingerprint_module_id = @module_id;",
            new { emp_code = employeeCode.Trim(), module_id = fingerprintModuleId.Trim() });
    }

    public PersonnelRecord? GetByFingerprintId(int fingerprintId, string fingerprintModuleId)
    {
        if (fingerprintId is < 1 or > 255 || string.IsNullOrWhiteSpace(fingerprintModuleId))
            return null;

        var entities = DbSession.ConfigDb.Ado.SqlQuery<PersonnelEntity>(@"
SELECT p.*
FROM personnel p
JOIN personnel_fingerprint_bindings b ON b.employee_code = p.employee_code
WHERE b.fingerprint_module_id = @module_id AND b.fingerprint_id = @fp_id
  AND p.is_active = 1;",
            new { module_id = fingerprintModuleId.Trim(), fp_id = fingerprintId });

        var entity = entities.FirstOrDefault();
        if (entity == null) return null;

        return new PersonnelRecord(
            entity.EmployeeCode,
            entity.EmployeeName,
            entity.Team ?? string.Empty,
            entity.FingerprintId,
            entity.IsActive,
            entity.Note ?? string.Empty,
            entity.CreatedUtcMs,
            entity.UpdatedUtcMs,
            entity.PasswordText ?? string.Empty);
    }

    public PersonnelRecord? GetByCode(string employeeCode)
    {
        if (string.IsNullOrWhiteSpace(employeeCode))
            return null;

        var entity = DbSession.ConfigDb.Queryable<PersonnelEntity>()
            .Where(e => e.EmployeeCode == employeeCode.Trim())
            .First();

        if (entity == null)
            return null;

        return new PersonnelRecord(
            entity.EmployeeCode,
            entity.EmployeeName,
            entity.Team ?? string.Empty,
            entity.FingerprintId,
            entity.IsActive,
            entity.Note ?? string.Empty,
            entity.CreatedUtcMs,
            entity.UpdatedUtcMs,
            entity.PasswordText ?? string.Empty);
    }

    public PersonnelRecord? GetByFingerprintId(int fingerprintId)
    {
        if (!IsValidFingerprintId(fingerprintId))
            return null;

        var entity = DbSession.ConfigDb.Queryable<PersonnelEntity>()
            .Where(e => e.FingerprintId == fingerprintId && e.IsActive)
            .First();

        if (entity == null)
            return null;

        return new PersonnelRecord(
            entity.EmployeeCode,
            entity.EmployeeName,
            entity.Team ?? string.Empty,
            entity.FingerprintId,
            entity.IsActive,
            entity.Note ?? string.Empty,
            entity.CreatedUtcMs,
            entity.UpdatedUtcMs,
            entity.PasswordText ?? string.Empty);
    }

    public void Upsert(
        string employeeCode,
        string employeeName,
        string? team = null,
        bool isActive = true,
        string? note = null,
        int? fingerprintId = null,
        string? passwordText = null)
    {
        if (string.IsNullOrWhiteSpace(employeeCode))
            throw new ArgumentException("Employee code is required.", nameof(employeeCode));
        if (string.IsNullOrWhiteSpace(employeeName))
            throw new ArgumentException("Employee name is required.", nameof(employeeName));

        ValidateFingerprintId(fingerprintId);
        var normalizedEmployeeCode = employeeCode.Trim();
        var normalizedPasswordText = passwordText ?? GetByCode(normalizedEmployeeCode)?.PasswordText ?? string.Empty;

        DbSession.ConfigDb.Storageable(new PersonnelEntity
        {
            EmployeeCode = normalizedEmployeeCode,
            EmployeeName = employeeName.Trim(),
            PasswordText = normalizedPasswordText,
            Team = string.IsNullOrWhiteSpace(team) ? null : team.Trim(),
            FingerprintId = fingerprintId,
            IsActive = isActive,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }).WhereColumns(e => e.EmployeeCode).ExecuteCommand();
    }

    public bool SetActive(string employeeCode, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(employeeCode))
            return false;
        if (!isActive && IsAdminCode(employeeCode))
            return false;

        var count = DbSession.ConfigDb.Updateable<PersonnelEntity>()
            .SetColumns(e => new PersonnelEntity
            {
                IsActive = isActive,
                UpdatedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            })
            .Where(e => e.EmployeeCode == employeeCode.Trim())
            .ExecuteCommand();

        return count > 0;
    }

    public bool Deactivate(string employeeCode)
    {
        if (IsAdminCode(employeeCode))
        {
            return false;
        }

        return SetActive(employeeCode, false);
    }

    public bool SetPassword(string employeeCode, string? passwordText)
    {
        if (string.IsNullOrWhiteSpace(employeeCode))
        {
            return false;
        }

        var count = DbSession.ConfigDb.Updateable<PersonnelEntity>()
            .SetColumns(e => new PersonnelEntity
            {
                PasswordText = passwordText ?? string.Empty,
                UpdatedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            })
            .Where(e => e.EmployeeCode == employeeCode.Trim())
            .ExecuteCommand();

        return count > 0;
    }

    public bool VerifyPassword(string employeeCode, string? passwordText, bool requireActive = true)
    {
        var record = GetByCode(employeeCode);
        if (record == null)
        {
            return false;
        }

        if (requireActive && !record.IsActive)
        {
            return false;
        }

        return string.Equals(record.PasswordText ?? string.Empty, passwordText ?? string.Empty, StringComparison.Ordinal);
    }

    public bool VerifyAdminPassword(string? passwordText)
    {
        EnsureDefaultAdmin();
        return VerifyPassword(AdminEmployeeCode, passwordText, requireActive: true);
    }

    public bool SetFingerprintId(string employeeCode, int? fingerprintId)
    {
        if (string.IsNullOrWhiteSpace(employeeCode))
            return false;

        ValidateFingerprintId(fingerprintId);

        var count = DbSession.ConfigDb.Updateable<PersonnelEntity>()
            .SetColumns(e => new PersonnelEntity
            {
                FingerprintId = fingerprintId,
                UpdatedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            })
            .Where(e => e.EmployeeCode == employeeCode.Trim())
            .ExecuteCommand();

        return count > 0;
    }

    public IReadOnlyList<string> ListTeams(bool includeInactive = false)
    {
        return DbSession.ConfigDb.Queryable<PersonnelEntity>()
            .Where(e => !string.IsNullOrWhiteSpace(e.Team))
            .Where(e => includeInactive || e.IsActive)
            .Select(e => e.Team!)
            .Distinct()
            .OrderBy(e => e)
            .ToList();
    }

    private static void ValidateFingerprintId(int? fingerprintId)
    {
        if (fingerprintId.HasValue && !IsValidFingerprintId(fingerprintId.Value))
            throw new ArgumentOutOfRangeException(nameof(fingerprintId), fingerprintId, "Fingerprint id must be between 1 and 255.");
    }

    private static bool IsValidFingerprintId(int fingerprintId) => fingerprintId is >= 1 and <= 255;

    public const string AdminEmployeeCode = "Admin";
    public const string AdminEmployeeName = "Admin";
    public const string AdminDefaultPassword = "Admin";

    public static bool IsAdminCode(string? employeeCode)
    {
        return string.Equals(employeeCode?.Trim(), AdminEmployeeCode, StringComparison.OrdinalIgnoreCase);
    }
}
