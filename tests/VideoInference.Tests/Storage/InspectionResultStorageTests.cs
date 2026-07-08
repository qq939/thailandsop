using VideoInferenceDemo.ImageInspection;

namespace VideoInferenceDemo.Tests.Storage;

[Collection("DbSession")]
public sealed class InspectionResultStorageTests : IDisposable
{
    private readonly string _root;

    public InspectionResultStorageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "inspection-result-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        DbSession.Reset();
    }

    [Fact]
    public async Task SqliteRepository_WritesInspectionCycleToDailyDatabase()
    {
        DbSession.InitializeSplit(Path.Combine(_root, "workspace_config.db"), Path.Combine(_root, "results"));
        var repository = new SqliteInspectionResultRepository();
        var item = CreateItem("trigger-sqlite");

        await repository.WriteAsync(item, InspectionResultMySqlSyncStatus.None);

        var dbPath = ResultDbSession.GetDbPathForDate(DateOnly.FromDateTime(item.Result.TriggerTime.LocalDateTime.Date));
        Assert.Equal(1, CountRows(dbPath, "inspection_cycles"));
        Assert.Equal(1, CountRows(dbPath, "inspection_roi_results"));
        Assert.Equal("None", ReadScalar<string>(dbPath, "SELECT mysql_sync_status FROM inspection_cycles LIMIT 1"));
        Assert.Equal(2, ReadScalar<int?>(dbPath, "SELECT defect_component_count FROM inspection_roi_results LIMIT 1"));
        Assert.Equal(128.0, ReadScalar<double?>(dbPath, "SELECT defect_max_area_px FROM inspection_roi_results LIMIT 1"));
        Assert.Equal(66.4, ReadScalar<double?>(dbPath, "SELECT defect_max_perimeter_px FROM inspection_roi_results LIMIT 1"));
        Assert.Equal(1.93, ReadScalar<double?>(dbPath, "SELECT defect_max_area_perimeter_ratio FROM inspection_roi_results LIMIT 1"));
        Assert.Contains("accepted=2", ReadScalar<string>(dbPath, "SELECT defect_summary_text FROM inspection_roi_results LIMIT 1") ?? string.Empty);
        Assert.Contains("#1 area=128", ReadScalar<string>(dbPath, "SELECT defect_components_text FROM inspection_roi_results LIMIT 1") ?? string.Empty);
    }

    [Fact]
    public async Task StorageService_WhenMySqlFails_WritesPendingSqliteFallback()
    {
        DbSession.InitializeSplit(Path.Combine(_root, "workspace_config.db"), Path.Combine(_root, "results"));
        using var service = new InspectionResultStorageService(() => new InspectionResultStorageOptions(
            InspectionResultStorageMode.MySqlPreferredWithSqliteFallback,
            "Server=127.0.0.1;Port=1;Database=image_inspection;Uid=root;Pwd=;Connection Timeout=1;",
            QueueCapacity: 8,
            RetryInterval: TimeSpan.FromHours(1)));
        var item = CreateItem("trigger-mysql-fallback");

        Assert.True(service.TryEnqueue(item));

        var dbPath = ResultDbSession.GetDbPathForDate(DateOnly.FromDateTime(item.Result.TriggerTime.LocalDateTime.Date));
        await WaitForAsync(() => CountRows(dbPath, "inspection_cycles") == 1);
        Assert.Equal("Pending", ReadScalar<string>(dbPath, "SELECT mysql_sync_status FROM inspection_cycles LIMIT 1"));
        Assert.False(string.IsNullOrWhiteSpace(ReadScalar<string?>(dbPath, "SELECT mysql_sync_error FROM inspection_cycles LIMIT 1")));
    }

    private static InspectionResultStorageItem CreateItem(string triggerId)
    {
        var result = new InspectionCycleResult
        {
            RecipeKey = new InspectionRecipeKey("A100", "appearance", "P01"),
            StationId = "station-1",
            TaskInstanceId = "task-1",
            CameraId = "cam-1",
            ActionType = "roi",
            TriggerId = triggerId,
            TriggerTime = DateTimeOffset.Now,
            Operator = new InspectionOperatorSnapshot("Admin", "Admin", string.Empty),
            Decision = InspectionCycleDecision.Ng,
            SummaryMessage = "test",
            ResolvedRois =
            [
                new RoiDefinition
                {
                    Id = "roi-1",
                    Name = "ROI 1",
                    CameraId = "cam-1",
                    CenterX = 0.5,
                    CenterY = 0.5,
                    Width = 0.2,
                    Height = 0.1,
                    ModelId = "model-1",
                    Enabled = true,
                    SortOrder = 1
                }
            ],
            RoiResults =
            [
                new InspectionRoiResult
                {
                    RoiId = "roi-1",
                    RoiName = "ROI 1",
                    ModelId = "model-1",
                    Decision = InspectionCycleDecision.Ng,
                    Score = 0.88,
                    DefectComponentCount = 2,
                    DefectMaxAreaPx = 128,
                    DefectMaxPerimeterPx = 66.4,
                    DefectMaxAreaPerimeterRatio = 1.93,
                    DefectSummaryText = "threshold=0.6; accepted=2; raw=5; maxArea=128; maxPerimeter=66.4; maxRatio=1.93; decision=NG",
                    DefectComponentsText = "#1 area=128 perimeter=66.4 ratio=1.93 bbox=(12,30,41,9) meanProb=0.71 maxProb=0.93",
                    Findings =
                    [
                        new InspectionFinding
                        {
                            Code = "ng",
                            Message = "NG",
                            Severity = InspectionCycleDecision.Ng
                        }
                    ]
                }
            ],
            Metadata = new Dictionary<string, string> { ["source"] = "unit-test" }
        };

        return new InspectionResultStorageItem(
            result,
            640,
            480,
            @"C:\images\frame.jpg",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["roi-1"] = @"C:\images\roi-1.jpg"
            });
    }

    private static int CountRows(string dbPath, string tableName)
    {
        if (!File.Exists(dbPath))
        {
            return 0;
        }

        using var db = DbSession.CreateScope(dbPath, _ => { }, foreignKeys: false);
        return db.Ado.SqlQuery<int>($"SELECT COUNT(*) FROM {tableName}").FirstOrDefault();
    }

    private static T? ReadScalar<T>(string dbPath, string sql)
    {
        using var db = DbSession.CreateScope(dbPath, _ => { }, foreignKeys: false);
        return db.Ado.SqlQuery<T>(sql).FirstOrDefault();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.True(condition());
    }

    public void Dispose()
    {
        DbSession.Reset();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
