using SqlSugar;

namespace VideoInferenceDemo;

public sealed class OperatorProductionKpiRow
{
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public long RunCount { get; set; }
    public long TotalWorkDurationMs { get; set; }
    public long OkCount { get; set; }
    public long NgCount { get; set; }
    public long FirstAssignedUtcMs { get; set; }
    public long LastEndedUtcMs { get; set; }
    public long TotalCount => OkCount + NgCount;
    public double YieldPercent => TotalCount > 0 ? OkCount * 100d / TotalCount : 0d;
    public double AvgWorkDurationMs => RunCount > 0 ? (double)TotalWorkDurationMs / RunCount : 0d;
}

public sealed class ProductionDashboardQueryService
{
    public ProductionDashboardQueryService(string dbPath)
    {
    }

    public IReadOnlyList<OperatorProductionKpiRow> QueryOperatorKpis(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        string? employeeCode = null,
        string? team = null,
        string? cameraId = null)
    {
        var fromUtcMs = fromInclusive.ToUnixTimeMilliseconds();
        var toUtcMs = toExclusive.ToUnixTimeMilliseconds();
        if (toUtcMs <= fromUtcMs)
            return Array.Empty<OperatorProductionKpiRow>();

        var fromDate = DateOnly.FromDateTime(fromInclusive.LocalDateTime.Date);
        var toDate = DateOnly.FromDateTime(toExclusive.LocalDateTime.Date);
        var lastDate = toDate.AddDays(-1);
        if (!ResultDbSession.IsInitialized)
        {
            return QueryOperatorKpis(DbSession.ResultDb, fromUtcMs, toUtcMs, employeeCode, team, cameraId);
        }

        var databaseInfos = ResultDbSession.ListDatabases(fromDate, lastDate);
        if (databaseInfos.Count == 0)
        {
            return Array.Empty<OperatorProductionKpiRow>();
        }

        var aggregate = new Dictionary<string, OperatorProductionKpiAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var databaseInfo in databaseInfos)
        {
            using var db = ResultDbSession.OpenScopeForDate(databaseInfo.Date);
            foreach (var row in QueryOperatorKpis(db, fromUtcMs, toUtcMs, employeeCode, team, cameraId))
            {
                if (!aggregate.TryGetValue(row.EmployeeCode, out var accumulator))
                {
                    accumulator = new OperatorProductionKpiAccumulator(row.EmployeeCode, row.EmployeeName);
                    aggregate[row.EmployeeCode] = accumulator;
                }

                accumulator.Add(row);
            }
        }

        return aggregate.Values
            .Select(item => item.ToRow())
            .OrderByDescending(row => row.TotalWorkDurationMs)
            .ThenBy(row => row.EmployeeCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<OperatorProductionKpiRow> QueryOperatorKpis(
        SqlSugar.ISqlSugarClient db,
        long fromUtcMs,
        long toUtcMs,
        string? employeeCode,
        string? team,
        string? cameraId)
    {
        return db.Ado.SqlQuery<OperatorProductionKpiRow>(@"
SELECT
  a.employee_code,
  COALESCE(NULLIF(TRIM(a.employee_name), ''), a.employee_code) AS employee_name,
  COUNT(DISTINCT a.run_uuid) AS run_count,
  SUM(
    CASE
      WHEN a.released_utc_ms IS NOT NULL AND a.released_utc_ms > a.assigned_utc_ms
        THEN a.released_utc_ms - a.assigned_utc_ms
      WHEN r.ended_utc_ms IS NOT NULL AND r.ended_utc_ms > r.started_utc_ms
        THEN r.ended_utc_ms - r.started_utc_ms
      ELSE 0
    END
  ) AS total_work_duration_ms,
  SUM(COALESCE(s.ok_count, 0)) AS ok_count,
  SUM(COALESCE(s.ng_count, 0)) AS ng_count,
  MIN(a.assigned_utc_ms) AS first_assigned_utc_ms,
  MAX(COALESCE(a.released_utc_ms, r.ended_utc_ms, a.assigned_utc_ms)) AS last_ended_utc_ms
FROM run_operator_assignments a
LEFT JOIN inference_runs r ON r.run_uuid = a.run_uuid
LEFT JOIN run_production_stats s ON s.run_uuid = a.run_uuid
WHERE
  a.assigned_utc_ms >= @from_utc_ms
  AND a.assigned_utc_ms < @to_utc_ms
  AND (@employee_code IS NULL OR a.employee_code = @employee_code)
  AND (@team IS NULL OR COALESCE(a.employee_team, '') = @team)
  AND (@camera_id IS NULL OR COALESCE(a.camera_id, '') = @camera_id)
GROUP BY
  a.employee_code,
  COALESCE(NULLIF(TRIM(a.employee_name), ''), a.employee_code)
ORDER BY
  total_work_duration_ms DESC,
  a.employee_code;",
        new
        {
            from_utc_ms = fromUtcMs,
            to_utc_ms = toUtcMs,
            employee_code = string.IsNullOrWhiteSpace(employeeCode) ? null : employeeCode.Trim(),
            team = string.IsNullOrWhiteSpace(team) ? null : team.Trim(),
            camera_id = string.IsNullOrWhiteSpace(cameraId) ? null : cameraId.Trim()
        });
    }

    private sealed class OperatorProductionKpiAccumulator
    {
        public OperatorProductionKpiAccumulator(string employeeCode, string employeeName)
        {
            EmployeeCode = employeeCode;
            EmployeeName = employeeName;
        }

        public string EmployeeCode { get; }
        public string EmployeeName { get; private set; }
        public long RunCount { get; private set; }
        public long TotalWorkDurationMs { get; private set; }
        public long OkCount { get; private set; }
        public long NgCount { get; private set; }
        public long FirstAssignedUtcMs { get; private set; }
        public long LastEndedUtcMs { get; private set; }

        public void Add(OperatorProductionKpiRow row)
        {
            if (string.IsNullOrWhiteSpace(EmployeeName) || EmployeeName == EmployeeCode)
            {
                EmployeeName = row.EmployeeName;
            }

            RunCount += row.RunCount;
            TotalWorkDurationMs += row.TotalWorkDurationMs;
            OkCount += row.OkCount;
            NgCount += row.NgCount;
            FirstAssignedUtcMs = FirstAssignedUtcMs == 0
                ? row.FirstAssignedUtcMs
                : Math.Min(FirstAssignedUtcMs, row.FirstAssignedUtcMs);
            LastEndedUtcMs = Math.Max(LastEndedUtcMs, row.LastEndedUtcMs);
        }

        public OperatorProductionKpiRow ToRow()
        {
            return new OperatorProductionKpiRow
            {
                EmployeeCode = EmployeeCode,
                EmployeeName = EmployeeName,
                RunCount = RunCount,
                TotalWorkDurationMs = TotalWorkDurationMs,
                OkCount = OkCount,
                NgCount = NgCount,
                FirstAssignedUtcMs = FirstAssignedUtcMs,
                LastEndedUtcMs = LastEndedUtcMs
            };
        }
    }
}
