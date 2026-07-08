using System.Collections.Concurrent;
using System.Diagnostics;

namespace VideoInferenceDemo;

public sealed class SqliteAnalysisResultWriter : IDisposable
{
    private readonly BlockingCollection<AnalysisResult> _queue;
    private readonly TimeSpan _flushInterval;
    private readonly int _maxBatch;
    private readonly Dictionary<string, long> _sourceIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _runIds = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private long _dropped;
    private long _lastDropWarnMs;

    public SqliteAnalysisResultWriter(int queueCapacity = 1000, TimeSpan? flushInterval = null, int maxBatch = 300)
    {
        _flushInterval = flushInterval ?? TimeSpan.FromSeconds(1);
        _maxBatch = Math.Max(20, maxBatch);
        _queue = new BlockingCollection<AnalysisResult>(new ConcurrentQueue<AnalysisResult>(), Math.Max(20, queueCapacity));
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

    public bool TryEnqueue(AnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (_queue.IsAddingCompleted)
        {
            ReportDropped();
            return false;
        }

        if (_queue.TryAdd(result))
            return true;

        ReportDropped();
        return false;
    }

    private void ReportDropped()
    {
        var count = Interlocked.Increment(ref _dropped);
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastDropWarnMs);
        if (now - last >= 10000 && Interlocked.CompareExchange(ref _lastDropWarnMs, now, last) == last)
        {
            CameraDiagnostics.Error("analysis-writer", $"Analysis result queue full, dropped {count} items.");
        }
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
            var batch = new List<AnalysisResult>(_maxBatch);
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
            TryAppendLog(ex);
        }
    }

    private void WriteBatch(List<AnalysisResult> batch)
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

                var nowUtcMs = item.FrameUtcMs > 0 ? item.FrameUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var sourceType = item.SourceKey.StartsWith("camera:", StringComparison.OrdinalIgnoreCase) ? "camera" : "video";
                var sourceId = GetOrCreateSourceId(db, item.SourceKey, sourceType, nowUtcMs);
                var runStartedUtcMs = item.RunStartedUtcMs > 0
                    ? item.RunStartedUtcMs
                    : nowUtcMs - Math.Max(0, item.PtsMs);
                var runId = GetOrCreateRunId(db, item.RunUuid, sourceId, runStartedUtcMs);

                db.Ado.ExecuteCommand(@"
INSERT INTO analysis_results (
  run_id, strategy_name, task_id, frame_index, pts_ms, frame_utc_ms,
  step, label, score_q1000, expected_state_code, current_state_code,
  ng_reason, debug_note, is_transition, is_reset, transition_ok,
  from_step, to_step, created_utc_ms)
VALUES (
  @run_id, @strategy_name, @task_id, @frame_index, @pts_ms, @frame_utc_ms,
  @step, @label, @score_q1000, @expected_state_code, @current_state_code,
  @ng_reason, @debug_note, @is_transition, @is_reset, @transition_ok,
  @from_step, @to_step, @created_utc_ms)
ON CONFLICT(run_id, strategy_name, frame_index) DO UPDATE SET
  task_id = excluded.task_id, pts_ms = excluded.pts_ms, frame_utc_ms = excluded.frame_utc_ms,
  step = excluded.step, label = excluded.label, score_q1000 = excluded.score_q1000,
  expected_state_code = excluded.expected_state_code, current_state_code = excluded.current_state_code,
  ng_reason = excluded.ng_reason, debug_note = excluded.debug_note,
  is_transition = excluded.is_transition, is_reset = excluded.is_reset,
  transition_ok = excluded.transition_ok, from_step = excluded.from_step,
  to_step = excluded.to_step, created_utc_ms = excluded.created_utc_ms;",
                    new
                    {
                        run_id = runId,
                        strategy_name = string.IsNullOrWhiteSpace(item.StrategyName) ? "unknown" : item.StrategyName,
                        task_id = ToDbText(item.TaskId),
                        frame_index = item.FrameIndex,
                        pts_ms = item.PtsMs,
                        frame_utc_ms = nowUtcMs,
                        step = ToDbInt(item.Step),
                        label = ToDbText(item.Label),
                        score_q1000 = ToDbScore(item.Score),
                        expected_state_code = ToDbText(item.ExpectedStateCode),
                        current_state_code = ToDbText(item.CurrentStateCode),
                        ng_reason = ToDbText(item.NgReason),
                        debug_note = ToDbText(item.DebugNote),
                        is_transition = item.IsTransition ? 1 : 0,
                        is_reset = item.IsReset ? 1 : 0,
                        transition_ok = item.TransitionOk.HasValue ? (item.TransitionOk.Value ? 1 : 0) : (int?)null,
                        from_step = ToDbInt(item.FromStep),
                        to_step = ToDbInt(item.ToStep),
                        created_utc_ms = nowUtcMs
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

    private static object ToDbInt(int? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object ToDbText(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static object ToDbScore(double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            return DBNull.Value;

        var scaled = (int)Math.Round(value.Value * 1000d, MidpointRounding.AwayFromZero);
        return Math.Clamp(scaled, 0, 1000);
    }

    private static void TryAppendLog(Exception ex)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "sqlite_analysis_writer.log"),
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {ex}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
