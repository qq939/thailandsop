using System.Collections.Concurrent;
using System.Diagnostics;

namespace VideoInferenceDemo;

public sealed class TcnFeatureWriter : IDisposable
{
    private readonly BlockingCollection<TcnFeatureEntry> _queue;
    private readonly TimeSpan _flushInterval;
    private readonly int _maxBatch;
    private readonly Dictionary<string, long> _sourceIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _runIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, int> _versionDimsById = new();
    private readonly Dictionary<string, long> _versionIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _versionLock = new();
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private long _dropped;

    public TcnFeatureWriter(int queueCapacity = 400, TimeSpan? flushInterval = null, int maxBatch = 200)
    {
        _flushInterval = flushInterval ?? TimeSpan.FromSeconds(1);
        _maxBatch = Math.Max(10, maxBatch);
        _queue = new BlockingCollection<TcnFeatureEntry>(new ConcurrentQueue<TcnFeatureEntry>(), Math.Max(10, queueCapacity));
        ResultDbSession.DatabaseChanged += OnResultDatabaseChanged;
    }

    public long DroppedCount => Interlocked.Read(ref _dropped);

    public long RegisterVersion(TcnFeatureVersion version)
    {
        if (version == null)
            throw new ArgumentNullException(nameof(version));

        lock (_versionLock)
        {
            var db = DbSession.ResultDb;
            db.Ado.BeginTran();
            try
            {
                var id = RegisterVersionCore(db, version);
                db.Ado.CommitTran();
                return id;
            }
            catch
            {
                db.Ado.RollbackTran();
                throw;
            }
        }
    }

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

    public bool TryEnqueue(TcnFeatureEntry entry)
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
            var batch = new List<TcnFeatureEntry>(_maxBatch);
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
                    Path.Combine(AppContext.BaseDirectory, "sqlite_feature_writer.log"),
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {ex}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }

    private void WriteBatch(List<TcnFeatureEntry> batch)
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

                if (item.FrameIndex < 0)
                {
                    continue;
                }

                long featureVersionId;
                lock (_versionLock)
                {
                    featureVersionId = item.FeatureVersion != null
                        ? RegisterVersionCore(db, item.FeatureVersion)
                        : item.FeatureVersionId;
                }
                if (featureVersionId <= 0 || !TryGetVersionDim(db, featureVersionId, out var dim))
                {
                    continue;
                }

                if (item.Features.Length != dim)
                {
                    continue;
                }

                var nowUtcMs = item.RunStartedUtcMs > 0
                    ? item.RunStartedUtcMs + Math.Max(0, item.PtsMs)
                    : item.CreatedUtcMs;
                var sourceType = item.SourceKey.StartsWith("camera:", StringComparison.OrdinalIgnoreCase) ? "camera" : "video";
                var sourceId = GetOrCreateSourceId(db, item.SourceKey, sourceType, nowUtcMs);
                var startedUtcMs = item.RunStartedUtcMs > 0 ? item.RunStartedUtcMs : nowUtcMs;
                var runId = GetOrCreateRunId(db, item.RunUuid, sourceId, startedUtcMs);

                db.Ado.ExecuteCommand(@"
INSERT INTO tcn_features (run_id, frame_index, pts_ms, feature_version_id, feature_blob, created_utc_ms)
VALUES (@run_id, @frame_index, @pts_ms, @feature_version_id, @feature_blob, @created_utc_ms)
ON CONFLICT(run_id, frame_index, feature_version_id) DO UPDATE SET
  pts_ms = excluded.pts_ms,
  feature_blob = excluded.feature_blob,
  created_utc_ms = excluded.created_utc_ms;",
                    new
                    {
                        run_id = runId,
                        frame_index = item.FrameIndex,
                        pts_ms = item.PtsMs,
                        feature_version_id = featureVersionId,
                        feature_blob = SerializeFeatures(item.Features),
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

    private bool TryGetVersionDim(SqlSugar.ISqlSugarClient db, long versionId, out int dim)
    {
        if (_versionDimsById.TryGetValue(versionId, out dim))
            return true;

        var result = db.Ado.SqlQuery<int?>(
            "SELECT feature_dim FROM tcn_feature_versions WHERE id = @id",
            new { id = versionId }).FirstOrDefault();

        if (!result.HasValue)
        {
            dim = 0;
            return false;
        }

        dim = result.Value;
        _versionDimsById[versionId] = dim;
        return true;
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
        lock (_versionLock)
        {
            _sourceIds.Clear();
            _runIds.Clear();
            _versionDimsById.Clear();
            _versionIds.Clear();
        }
    }

    private long RegisterVersionCore(SqlSugar.ISqlSugarClient db, TcnFeatureVersion version)
    {
        if (_versionIds.TryGetValue(version.Key, out var cached))
            return cached;

        db.Ado.ExecuteCommand(@"
INSERT OR IGNORE INTO tcn_feature_versions (name, version, config_json, feature_dim, created_utc_ms)
VALUES (@name, @version, @config_json, @feature_dim, @created_utc_ms);",
            new
            {
                name = version.Name,
                version = version.Version,
                config_json = (object?)version.ConfigJson ?? DBNull.Value,
                feature_dim = version.FeatureDim,
                created_utc_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

        var row = db.Ado.SqlQuery<TcnFeatureVersionRow>(
            "SELECT id, feature_dim FROM tcn_feature_versions WHERE name = @name AND version = @version",
            new { name = version.Name, version = version.Version }).First();

        if (row == null)
        {
            throw new InvalidOperationException("Failed to load tcn_feature_versions row.");
        }

        if (row.FeatureDim != version.FeatureDim)
        {
            throw new InvalidOperationException($"FeatureDim mismatch for {version.Key}. Expected {row.FeatureDim}, got {version.FeatureDim}.");
        }

        _versionIds[version.Key] = row.Id;
        _versionDimsById[row.Id] = row.FeatureDim;
        return row.Id;
    }

    private static byte[] SerializeFeatures(float[] features)
    {
        if (features.Length == 0)
            return Array.Empty<byte>();

        var bytes = new byte[features.Length * sizeof(float)];
        Buffer.BlockCopy(features, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private sealed class TcnFeatureVersionRow
    {
        public long Id { get; set; }
        public int FeatureDim { get; set; }
    }
}
