namespace VideoInferenceDemo;

public sealed record RunProductionStatsRecord(
    string RunUuid,
    long OkCount,
    long NgCount,
    long UpdatedUtcMs)
{
    public long TotalCount => OkCount + NgCount;
    public double YieldPercent => TotalCount > 0 ? OkCount * 100d / TotalCount : 0d;
}

public sealed class RunProductionStatsRepository
{
    public RunProductionStatsRepository(string dbPath)
    {
    }

    public void Upsert(string runUuid, long okCount, long ngCount, long? updatedUtcMs = null)
    {
        if (string.IsNullOrWhiteSpace(runUuid))
            throw new ArgumentException("Run uuid is required.", nameof(runUuid));

        var updated = updatedUtcMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        DbSession.ResultDb.Storageable(new RunProductionStatsEntity
        {
            RunUuid = runUuid.Trim(),
            OkCount = Math.Max(0, okCount),
            NgCount = Math.Max(0, ngCount),
            UpdatedUtcMs = updated
        }).ExecuteCommand();
    }

    public RunProductionStatsRecord? GetByRunUuid(string runUuid)
    {
        if (string.IsNullOrWhiteSpace(runUuid))
            return null;

        var entity = DbSession.ResultDb.Queryable<RunProductionStatsEntity>()
            .Where(s => s.RunUuid == runUuid.Trim())
            .First();

        if (entity == null)
            return null;

        return new RunProductionStatsRecord(
            entity.RunUuid,
            entity.OkCount,
            entity.NgCount,
            entity.UpdatedUtcMs);
    }
}
