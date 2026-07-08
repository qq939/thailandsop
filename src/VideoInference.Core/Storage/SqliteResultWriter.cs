using System.Collections.Concurrent;
using System.Diagnostics;

namespace VideoInferenceDemo;

public sealed class SqliteResultWriter : ILegacyDetectionResultSink, IDisposable
{
    private readonly BlockingCollection<FrameDetections> _queue;
    private readonly TimeSpan _flushInterval;
    private readonly int _maxBatch;
    private readonly bool _enableRawDetections;
    private readonly Dictionary<string, long> _modelIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _sourceIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _runIds = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private long _dropped;

    public SqliteResultWriter(bool enableRawDetections = false, int queueCapacity = 600, TimeSpan? flushInterval = null, int maxBatch = 200)
    {
        _enableRawDetections = enableRawDetections;
        _flushInterval = flushInterval ?? TimeSpan.FromSeconds(1);
        _maxBatch = Math.Max(10, maxBatch);
        _queue = new BlockingCollection<FrameDetections>(new ConcurrentQueue<FrameDetections>(), Math.Max(10, queueCapacity));
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

    public bool TryEnqueue(FrameDetections batch)
    {
        if (_queue.IsAddingCompleted)
        {
            Interlocked.Increment(ref _dropped);
            return false;
        }

        if (_queue.TryAdd(batch))
            return true;

        Interlocked.Increment(ref _dropped);
        return false;
    }

    public void MarkRunEnded(string runUuid, string status = "stopped", long? endedUtcMs = null)
    {
        if (string.IsNullOrWhiteSpace(runUuid))
            return;

        try
        {
            DbSession.ResultDb.Ado.ExecuteCommand(@"
UPDATE inference_runs
SET
  status = @status,
  ended_utc_ms = CASE
    WHEN ended_utc_ms IS NULL THEN @ended_utc_ms
    WHEN ended_utc_ms < @ended_utc_ms THEN @ended_utc_ms
    ELSE ended_utc_ms
  END
WHERE run_uuid = @run_uuid;",
                new
                {
                    status = string.IsNullOrWhiteSpace(status) ? "stopped" : status,
                    ended_utc_ms = endedUtcMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    run_uuid = runUuid
                });
        }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "sqlite_writer.log"),
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} mark-run-ended {ex}{Environment.NewLine}");
            }
            catch
            {
            }
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
            var batch = new List<FrameDetections>(_maxBatch);
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
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "sqlite_writer.log"),
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {ex}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }

    private void WriteBatch(List<FrameDetections> batch)
    {
        var db = DbSession.ResultDb;
        try
        {
            db.Ado.BeginTran();

            foreach (var item in batch)
            {
                var frame = item.Frame;
                if (string.IsNullOrWhiteSpace(frame.SourceId) || string.IsNullOrWhiteSpace(frame.RunUuid))
                {
                    continue;
                }

                var nowUtcMs = frame.FrameUtcMs > 0 ? frame.FrameUtcMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var sourceId = GetOrCreateSourceId(db, frame.SourceId, frame.SourceType, frame.Width, frame.Height, nowUtcMs);
                var modelId = GetOrCreateModelId(db, frame.ModelVersion, nowUtcMs);
                var startedUtcMs = frame.RunStartedUtcMs > 0 ? frame.RunStartedUtcMs : nowUtcMs;
                var runId = GetOrCreateRunId(db, frame.RunUuid, sourceId, startedUtcMs);

                db.Ado.ExecuteCommand(@"
INSERT OR IGNORE INTO run_models (run_id, model_id, role, created_utc_ms)
VALUES (@run_id, @model_id, 'primary', @created_utc_ms);",
                    new { run_id = runId, model_id = modelId, created_utc_ms = nowUtcMs });

                if (_enableRawDetections && item.Detections.Count > 0)
                {
                    foreach (var det in item.Detections)
                    {
                        if (det.ClassId < 0)
                            continue;

                        var x = ToPixelStart(det.X1);
                        var y = ToPixelStart(det.Y1);
                        var w = Math.Max(1, ToPixelEnd(det.X2) - x);
                        var h = Math.Max(1, ToPixelEnd(det.Y2) - y);

                        db.Ado.ExecuteCommand(@"
INSERT INTO raw_det (run_id, model_id, frame_index, class_id, score_q1000, x_px, y_px, w_px, h_px)
VALUES (@run_id, @model_id, @frame_index, @class_id, @score_q1000, @x_px, @y_px, @w_px, @h_px);",
                            new
                            {
                                run_id = runId,
                                model_id = modelId,
                                frame_index = frame.FrameIndex,
                                class_id = det.ClassId,
                                score_q1000 = (int)Math.Clamp(det.Score * 1000f, 0, 1000),
                                x_px = x,
                                y_px = y,
                                w_px = w,
                                h_px = h
                            });
                    }
                }
            }

            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
    }

    private long GetOrCreateSourceId(SqlSugar.ISqlSugarClient db, string sourceKey, string sourceType, int width, int height, long nowUtcMs)
    {
        if (_sourceIds.TryGetValue(sourceKey, out var id))
            return id;

        db.Ado.ExecuteCommand(@"
INSERT INTO sources (source_key, source_type, width_px, height_px, created_utc_ms, updated_utc_ms)
VALUES (@source_key, @source_type, @width_px, @height_px, @now_utc_ms, @now_utc_ms)
ON CONFLICT(source_key) DO UPDATE SET
  source_type = CASE WHEN excluded.source_type IS NULL OR excluded.source_type = '' THEN source_type ELSE excluded.source_type END,
  width_px = CASE WHEN (width_px IS NULL OR width_px = 0) AND excluded.width_px > 0 THEN excluded.width_px ELSE width_px END,
  height_px = CASE WHEN (height_px IS NULL OR height_px = 0) AND excluded.height_px > 0 THEN excluded.height_px ELSE height_px END,
  updated_utc_ms = excluded.updated_utc_ms;",
            new
            {
                source_key = sourceKey,
                source_type = string.IsNullOrWhiteSpace(sourceType) ? (object)DBNull.Value : sourceType,
                width_px = width > 0 ? width : 0,
                height_px = height > 0 ? height : 0,
                now_utc_ms = nowUtcMs
            });

        id = db.Ado.SqlQuery<long>("SELECT id FROM sources WHERE source_key = @source_key", new { source_key = sourceKey }).First();
        _sourceIds[sourceKey] = id;
        return id;
    }

    private long GetOrCreateModelId(SqlSugar.ISqlSugarClient db, string? modelPath, long nowUtcMs)
    {
        var key = string.IsNullOrWhiteSpace(modelPath) ? "det:unknown" : modelPath;
        if (_modelIds.TryGetValue(key, out var id))
            return id;

        db.Ado.ExecuteCommand(@"
INSERT INTO models (model_key, model_path, task_type, created_utc_ms, updated_utc_ms)
VALUES (@model_key, @model_path, 'det', @now_utc_ms, @now_utc_ms)
ON CONFLICT(model_key) DO UPDATE SET
  model_path = CASE WHEN excluded.model_path IS NULL OR excluded.model_path = '' THEN model_path ELSE excluded.model_path END,
  updated_utc_ms = excluded.updated_utc_ms;",
            new
            {
                model_key = key,
                model_path = string.IsNullOrWhiteSpace(modelPath) ? (object)DBNull.Value : modelPath,
                now_utc_ms = nowUtcMs
            });

        id = db.Ado.SqlQuery<long>("SELECT id FROM models WHERE model_key = @model_key", new { model_key = key }).First();
        _modelIds[key] = id;
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
  started_utc_ms = CASE WHEN started_utc_ms <= excluded.started_utc_ms THEN started_utc_ms ELSE excluded.started_utc_ms END;",
            new { run_uuid = runUuid, source_id = sourceId, started_utc_ms = startedUtcMs });

        id = db.Ado.SqlQuery<long>("SELECT id FROM inference_runs WHERE run_uuid = @run_uuid", new { run_uuid = runUuid }).First();
        _runIds[runUuid] = id;
        return id;
    }

    private void OnResultDatabaseChanged(object? sender, EventArgs e)
    {
        lock (_modelIds)
        {
            _modelIds.Clear();
            _sourceIds.Clear();
            _runIds.Clear();
        }
    }

    private static int ToPixelStart(float coord) => (int)(coord + 0.5f);

    private static int ToPixelEnd(float coord) => (int)(coord + 0.5f);
}
