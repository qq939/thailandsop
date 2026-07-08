using System.Collections.Concurrent;
using System.Diagnostics;

namespace VideoInferenceDemo;

public sealed class TcnLabelWriter : IDisposable
{
    private readonly BlockingCollection<TcnLabelEntry> _queue;
    private readonly TimeSpan _flushInterval;
    private readonly int _maxBatch;
    private readonly Dictionary<string, long> _sourceIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _runIds = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private long _dropped;

    public TcnLabelWriter(int queueCapacity = 400, TimeSpan? flushInterval = null, int maxBatch = 200)
    {
        _flushInterval = flushInterval ?? TimeSpan.FromSeconds(1);
        _maxBatch = Math.Max(10, maxBatch);
        _queue = new BlockingCollection<TcnLabelEntry>(new ConcurrentQueue<TcnLabelEntry>(), Math.Max(10, queueCapacity));
        ResultDbSession.DatabaseChanged += OnResultDatabaseChanged;
    }

    public long DroppedCount => Interlocked.Read(ref _dropped);

    public void Start()
    {
        if (_worker != null)
            return;

        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Stop()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts == null)
            return;

        try { cts.Cancel(); } catch { }
        try { _queue.CompleteAdding(); } catch { }
        try { _worker?.Wait(2000); } catch { }
        _worker = null;
    }

    public bool TryEnqueue(TcnLabelEntry entry)
    {
        if (_queue.IsAddingCompleted)
        {
            Interlocked.Increment(ref _dropped);
            return false;
        }

        if (_queue.TryAdd(entry))
            return true;

        Interlocked.Increment(ref _dropped);
        return false;
    }

    public void Dispose()
    {
        Stop();
        ResultDbSession.DatabaseChanged -= OnResultDatabaseChanged;
        _queue.Dispose();
    }

    private void RunAsync(CancellationToken ct)
    {
        try
        {
            var batch = new List<TcnLabelEntry>(_maxBatch);
            var sw = Stopwatch.StartNew();
            var nextFlushMs = sw.ElapsedMilliseconds + (long)_flushInterval.TotalMilliseconds;

            while (!ct.IsCancellationRequested || _queue.Count > 0)
            {
                if (_queue.TryTake(out var item, 50))
                {
                    batch.Add(item);
                }

                var now = sw.ElapsedMilliseconds;
                if (batch.Count > 0 && (batch.Count >= _maxBatch || now >= nextFlushMs))
                {
                    WriteBatch(batch);
                    batch.Clear();
                    nextFlushMs = now + (long)_flushInterval.TotalMilliseconds;
                }
            }

            if (batch.Count > 0)
            {
                WriteBatch(batch);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(AppContext.BaseDirectory, "sqlite_label_writer.log"),
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {ex}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }

    private void WriteBatch(List<TcnLabelEntry> batch)
    {
        var db = DbSession.ResultDb;
        try
        {
            db.Ado.BeginTran();

            foreach (var item in batch)
            {
                if (string.IsNullOrWhiteSpace(item.RunUuid) || string.IsNullOrWhiteSpace(item.SourceKey))
                {
                    continue;
                }

                var startPtsMs = item.StartMs;
                var endPtsMs = item.EndMs;
                if (endPtsMs <= startPtsMs)
                {
                    continue;
                }

                var nowUtcMs = item.CreatedUtcMs;
                var sourceType = item.SourceKey.StartsWith("camera:", StringComparison.OrdinalIgnoreCase) ? "camera" : "video";
                var sourceId = GetOrCreateSourceId(db, item.SourceKey, sourceType, nowUtcMs);
                var runStartedUtcMs = item.RunStartedUtcMs > 0
                    ? item.RunStartedUtcMs
                    : nowUtcMs - Math.Max(0, endPtsMs);
                var runId = GetOrCreateRunId(db, item.RunUuid, sourceId, runStartedUtcMs);
                var startUtcMs = runStartedUtcMs + Math.Max(0, startPtsMs);
                var endUtcMs = runStartedUtcMs + Math.Max(0, endPtsMs);
                if (endUtcMs <= startUtcMs)
                {
                    endUtcMs = startUtcMs + 1;
                }

                db.Ado.ExecuteCommand(@"
INSERT INTO fsm_labels (
  run_id, step_index, label, source_type, score_q,
  start_pts_ms, end_pts_ms, start_utc_ms, end_utc_ms,
  created_utc_ms, updated_utc_ms)
VALUES (
  @run_id, @step_index, @label, @source_type, @score_q,
  @start_pts_ms, @end_pts_ms, @start_utc_ms, @end_utc_ms,
  @created_utc_ms, @updated_utc_ms)
ON CONFLICT(run_id, source_type, step_index, label, start_utc_ms, end_utc_ms) DO UPDATE SET
  score_q = COALESCE(excluded.score_q, fsm_labels.score_q),
  start_pts_ms = excluded.start_pts_ms,
  end_pts_ms = excluded.end_pts_ms,
  updated_utc_ms = excluded.updated_utc_ms;",
                    new
                    {
                        run_id = runId,
                        step_index = NormalizeStepIndex(item.StepIndex),
                        label = string.IsNullOrWhiteSpace(item.Label) ? "unknown" : item.Label,
                        source_type = NormalizeSourceType(item.SourceType),
                        score_q = ScoreToQ(item.Score),
                        start_pts_ms = startPtsMs,
                        end_pts_ms = endPtsMs,
                        start_utc_ms = startUtcMs,
                        end_utc_ms = endUtcMs,
                        created_utc_ms = nowUtcMs,
                        updated_utc_ms = nowUtcMs
                    });
            }

            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
    }

    private long GetOrCreateSourceId(SqlSugar.ISqlSugarClient db, string sourceKey, string sourceType, long nowUtcMs)
    {
        if (_sourceIds.TryGetValue(sourceKey, out var id))
            return id;

        db.Ado.ExecuteCommand(@"
INSERT INTO sources (source_key, source_type, width_px, height_px, created_utc_ms, updated_utc_ms)
VALUES (@source_key, @source_type, 0, 0, @now_utc_ms, @now_utc_ms)
ON CONFLICT(source_key) DO UPDATE SET
  source_type = CASE
    WHEN excluded.source_type IS NULL OR excluded.source_type = '' THEN sources.source_type
    ELSE excluded.source_type
  END,
  updated_utc_ms = excluded.updated_utc_ms;",
            new { source_key = sourceKey, source_type = sourceType, now_utc_ms = nowUtcMs });

        id = db.Ado.SqlQuery<long>("SELECT id FROM sources WHERE source_key = @source_key", new { source_key = sourceKey }).First();
        _sourceIds[sourceKey] = id;
        return id;
    }

    private long GetOrCreateRunId(SqlSugar.ISqlSugarClient db, string runUuid, long sourceId, long startedUtcMs)
    {
        if (_runIds.TryGetValue(runUuid, out var id))
            return id;

        db.Ado.ExecuteCommand(@"
INSERT INTO inference_runs (run_uuid, source_id, started_utc_ms, status)
VALUES (@run_uuid, @source_id, @started_utc_ms, 'running')
ON CONFLICT(run_uuid) DO UPDATE SET
  source_id = excluded.source_id,
  started_utc_ms = CASE
    WHEN inference_runs.started_utc_ms <= excluded.started_utc_ms THEN inference_runs.started_utc_ms
    ELSE excluded.started_utc_ms
  END;",
            new { run_uuid = runUuid, source_id = sourceId, started_utc_ms = startedUtcMs });

        id = db.Ado.SqlQuery<long>("SELECT id FROM inference_runs WHERE run_uuid = @run_uuid", new { run_uuid = runUuid }).First();
        _runIds[runUuid] = id;
        return id;
    }

    private void OnResultDatabaseChanged(object? sender, EventArgs e)
    {
        lock (_sourceIds)
        {
            _sourceIds.Clear();
            _runIds.Clear();
        }
    }

    private static int NormalizeStepIndex(int? stepIndex)
    {
        if (!stepIndex.HasValue)
            return -1;

        return stepIndex.Value < 0 ? -1 : stepIndex.Value;
    }

    private static string NormalizeSourceType(string sourceType)
    {
        if (string.IsNullOrWhiteSpace(sourceType))
            return "manual";

        return sourceType.Trim().ToLowerInvariant();
    }

    private static object ScoreToQ(float? score)
    {
        if (!score.HasValue || float.IsNaN(score.Value) || float.IsInfinity(score.Value))
            return DBNull.Value;

        var scaled = (int)Math.Round(score.Value * 10000.0, MidpointRounding.AwayFromZero);
        return Math.Clamp(scaled, 0, 10000);
    }
}
