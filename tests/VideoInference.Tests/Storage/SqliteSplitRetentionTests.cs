namespace VideoInferenceDemo.Tests.Storage;

[Collection("DbSession")]
public sealed class SqliteSplitRetentionTests : IDisposable
{
    private readonly string _root;

    public SqliteSplitRetentionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "VideoInferenceDemo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        DbSession.Reset();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public void InitializeSplit_CreatesConfigAndTodayResultDatabase()
    {
        var configPath = Path.Combine(_root, "workspace_config.db");
        var resultsRoot = Path.Combine(_root, "results");

        DbSession.InitializeSplit(configPath, resultsRoot);
        _ = DbSession.ConfigDb.Queryable<CameraSettingsStateEntity>().Count();
        _ = DbSession.ResultDb.Ado.SqlQuery<int>("SELECT COUNT(*) FROM sources").FirstOrDefault();

        var todayPath = Path.Combine(resultsRoot, $"{DateTime.Now:yyyy-MM-dd}.db");
        Assert.True(File.Exists(configPath));
        Assert.True(File.Exists(todayPath));
        Assert.True(TableExists(configPath, "camera_profiles"));
        Assert.False(TableExists(configPath, "inference_runs"));
        Assert.True(TableExists(todayPath, "inference_runs"));
        Assert.False(TableExists(todayPath, "personnel"));
    }

    [Fact]
    public void LegacyMigration_CopiesConfigTablesOnly_AndKeepsLegacyDatabase()
    {
        var legacyPath = Path.Combine(_root, "inference.db");
        var configPath = Path.Combine(_root, "workspace_config.db");
        DbSession.Initialize(legacyPath);
        var personnel = new PersonnelRepository(legacyPath);
        personnel.Upsert("E001", "Operator A", "A");
        DbSession.ResultDb.Ado.ExecuteCommand(@"
INSERT INTO sources (source_key, source_type, created_utc_ms, updated_utc_ms)
VALUES ('camera:1', 'camera', 1, 1);");
        DbSession.Reset();

        var migrated = SqliteWorkspaceMigrator.TryMigrateLegacyConfig(legacyPath, configPath);

        Assert.True(migrated);
        Assert.True(File.Exists(legacyPath));
        Assert.True(File.Exists(configPath));
        Assert.True(TableExists(configPath, "personnel"));
        Assert.False(TableExists(configPath, "sources"));

        DbSession.InitializeSplit(configPath, Path.Combine(_root, "results"));
        var migratedPersonnel = new PersonnelRepository(configPath).GetByCode("E001");
        Assert.NotNull(migratedPersonnel);
        Assert.Equal("Operator A", migratedPersonnel!.EmployeeName);
    }

    [Fact]
    public async Task ResultWriter_WritesToDailyDatabase_AndRollsOverWhenDateChanges()
    {
        var day = new DateTimeOffset(2026, 6, 6, 8, 0, 0, TimeSpan.Zero);
        var current = day;
        DbSession.InitializeSplit(Path.Combine(_root, "workspace_config.db"), Path.Combine(_root, "results"));
        ResultDbSession.Reset();
        ResultDbSession.Initialize(Path.Combine(_root, "results"), () => current);

        using var writer = new SqliteResultWriter(
            enableRawDetections: true,
            flushInterval: TimeSpan.FromMilliseconds(20),
            maxBatch: 10);
        writer.Start();
        writer.TryEnqueue(CreateDetection("run-1", day.ToUnixTimeMilliseconds()));
        await WaitForAsync(() => CountRows(ResultDbSession.GetDbPathForDate(DateOnly.FromDateTime(day.Date)), "inference_runs") == 1);

        current = day.AddDays(1);
        writer.TryEnqueue(CreateDetection("run-2", current.ToUnixTimeMilliseconds()));
        await WaitForAsync(() => CountRows(ResultDbSession.GetDbPathForDate(DateOnly.FromDateTime(current.Date)), "inference_runs") == 1);

        Assert.Equal(1, CountRows(ResultDbSession.GetDbPathForDate(DateOnly.FromDateTime(day.Date)), "inference_runs"));
        Assert.Equal(1, CountRows(ResultDbSession.GetDbPathForDate(DateOnly.FromDateTime(current.Date)), "inference_runs"));
    }

    [Fact]
    public async Task Cleanup_DeletesExpiredResultDatabasesAndMedia_ButKeepsTodayAndRecent()
    {
        var resultsRoot = Path.Combine(_root, "results");
        var recordingsRoot = Path.Combine(_root, "Recordings");
        var imagesRoot = Path.Combine(_root, "InspectionImages");
        Directory.CreateDirectory(resultsRoot);
        Directory.CreateDirectory(recordingsRoot);
        Directory.CreateDirectory(imagesRoot);
        var today = DateTime.Now.Date;
        var oldDate = today.AddDays(-91).ToString("yyyy-MM-dd");
        var recentDate = today.AddDays(-30).ToString("yyyy-MM-dd");
        var todayText = today.ToString("yyyy-MM-dd");

        foreach (var suffix in new[] { ".db", ".db-wal", ".db-shm" })
        {
            File.WriteAllText(Path.Combine(resultsRoot, oldDate + suffix), "old");
            File.WriteAllText(Path.Combine(resultsRoot, recentDate + suffix), "recent");
        }

        var oldRecording = Path.Combine(recordingsRoot, oldDate);
        var recentRecording = Path.Combine(recordingsRoot, recentDate);
        var todayImages = Path.Combine(imagesRoot, todayText);
        Directory.CreateDirectory(oldRecording);
        Directory.CreateDirectory(recentRecording);
        Directory.CreateDirectory(Path.Combine(imagesRoot, oldDate));
        Directory.CreateDirectory(todayImages);
        File.WriteAllText(Path.Combine(oldRecording, "a.mkv"), "old");
        File.WriteAllText(Path.Combine(recentRecording, "a.mkv"), "recent");

        var legacyImage = Path.Combine(imagesRoot, "A100", "task", "P01", "old.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyImage)!);
        File.WriteAllText(legacyImage, "old");
        File.SetLastWriteTime(legacyImage, today.AddDays(-120));

        using var service = new RetentionCleanupService(new RetentionCleanupOptions
        {
            ResultsDirectory = resultsRoot,
            RecordingDirectories = [recordingsRoot],
            InspectionImageDirectories = [imagesRoot],
            RetentionDays = 90,
            EnableAutoCleanup = true,
            BatchSize = 1,
            BatchDelay = TimeSpan.Zero
        });

        await service.RunOnceAsync();

        Assert.False(File.Exists(Path.Combine(resultsRoot, oldDate + ".db")));
        Assert.False(File.Exists(Path.Combine(resultsRoot, oldDate + ".db-wal")));
        Assert.False(File.Exists(Path.Combine(resultsRoot, oldDate + ".db-shm")));
        Assert.True(File.Exists(Path.Combine(resultsRoot, recentDate + ".db")));
        Assert.False(Directory.Exists(oldRecording));
        Assert.True(Directory.Exists(recentRecording));
        Assert.False(Directory.Exists(Path.Combine(imagesRoot, oldDate)));
        Assert.True(Directory.Exists(todayImages));
        Assert.False(File.Exists(legacyImage));
    }

    private static FrameDetections CreateDetection(string runUuid, long utcMs)
    {
        return new FrameDetections(
            new FrameEntity
            {
                SourceId = "camera:test",
                SourceType = "camera",
                RunUuid = runUuid,
                RunStartedUtcMs = utcMs,
                FrameIndex = 1,
                TimestampMs = 1,
                FrameUtcMs = utcMs,
                Width = 640,
                Height = 480,
                ModelVersion = "model:test"
            },
            new[]
            {
                new DetectionEntity
                {
                    ClassId = 1,
                    Score = 0.9f,
                    X1 = 10,
                    Y1 = 20,
                    X2 = 50,
                    Y2 = 80
                }
            });
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.True(predicate());
    }

    private static bool TableExists(string dbPath, string tableName)
    {
        if (!File.Exists(dbPath))
        {
            return false;
        }

        using var db = DbSession.CreateScope(dbPath, _ => { }, foreignKeys: false);
        return db.Ado.SqlQuery<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;",
            new { name = tableName }).FirstOrDefault() > 0;
    }

    private static int CountRows(string dbPath, string tableName)
    {
        if (!File.Exists(dbPath))
        {
            return 0;
        }

        using var db = DbSession.CreateScope(dbPath, _ => { }, foreignKeys: false);
        return db.Ado.SqlQuery<int>($"SELECT COUNT(*) FROM {tableName};").FirstOrDefault();
    }
}
