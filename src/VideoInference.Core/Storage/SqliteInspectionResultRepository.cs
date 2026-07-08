using SqlSugar;

namespace VideoInferenceDemo;

public sealed class SqliteInspectionResultRepository
{
    public Task WriteAsync(
        InspectionResultStorageItem item,
        InspectionResultMySqlSyncStatus syncStatus,
        string? syncError = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var date = DateOnly.FromDateTime(item.Result.TriggerTime.LocalDateTime.Date);
        var db = ResultDbSession.GetDbForDate(date);
        WriteRows(db, InspectionResultRowMapper.Map(item, syncStatus, syncError));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<InspectionResultRowSet>> ReadPendingAsync(
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var today = DateOnly.FromDateTime(DateTime.Now.Date);
        var from = today.AddDays(-Math.Max(1, 3650));
        var databases = ResultDbSession.ListDatabases(from, today);
        var rows = new List<InspectionResultRowSet>();
        foreach (var database in databases.OrderBy(item => item.Date))
        {
            if (rows.Count >= maxCount)
            {
                break;
            }

            using var db = ResultDbSession.OpenScopeForDate(database.Date);
            var pending = db.Ado.SqlQuery<InspectionCycleRow>(@"
SELECT
  cycle_uuid AS CycleUuid,
  trigger_utc_ms AS TriggerUtcMs,
  trigger_local_date AS TriggerLocalDate,
  product_model AS ProductModel,
  task_id AS TaskId,
  position_no AS PositionNo,
  station_id AS StationId,
  task_instance_id AS TaskInstanceId,
  camera_id AS CameraId,
  action_type AS ActionType,
  trigger_id AS TriggerId,
  decision AS Decision,
  operator_code AS OperatorCode,
  operator_name AS OperatorName,
  summary_message AS SummaryMessage,
  metadata_json AS MetadataJson,
  image_width AS ImageWidth,
  image_height AS ImageHeight,
  image_path AS ImagePath,
  created_utc_ms AS CreatedUtcMs,
  mysql_sync_status AS MySqlSyncStatus,
  mysql_sync_error AS MySqlSyncError,
  mysql_synced_utc_ms AS MySqlSyncedUtcMs
FROM inspection_cycles
WHERE mysql_sync_status IN ('Pending', 'Failed')
ORDER BY trigger_utc_ms
LIMIT @limit", new { limit = maxCount - rows.Count });

            foreach (var cycle in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                rows.Add(new InspectionResultRowSet(cycle, ReadRois(db, cycle.CycleUuid)));
            }
        }

        return Task.FromResult<IReadOnlyList<InspectionResultRowSet>>(rows);
    }

    public Task MarkSyncedAsync(string cycleUuid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var today = DateOnly.FromDateTime(DateTime.Now.Date);
        var from = today.AddDays(-Math.Max(1, 3650));
        var syncedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var database in ResultDbSession.ListDatabases(from, today))
        {
            using var db = ResultDbSession.OpenScopeForDate(database.Date);
            var affected = db.Ado.ExecuteCommand(@"
UPDATE inspection_cycles
SET mysql_sync_status = 'Synced',
    mysql_sync_error = NULL,
    mysql_synced_utc_ms = @synced_utc_ms
WHERE cycle_uuid = @cycle_uuid",
                new { cycle_uuid = cycleUuid, synced_utc_ms = syncedUtcMs });
            if (affected > 0)
            {
                break;
            }
        }

        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(string cycleUuid, string error, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var today = DateOnly.FromDateTime(DateTime.Now.Date);
        var from = today.AddDays(-Math.Max(1, 3650));
        foreach (var database in ResultDbSession.ListDatabases(from, today))
        {
            using var db = ResultDbSession.OpenScopeForDate(database.Date);
            var affected = db.Ado.ExecuteCommand(@"
UPDATE inspection_cycles
SET mysql_sync_status = 'Failed',
    mysql_sync_error = @error
WHERE cycle_uuid = @cycle_uuid",
                new { cycle_uuid = cycleUuid, error });
            if (affected > 0)
            {
                break;
            }
        }

        return Task.CompletedTask;
    }

    internal static void WriteRows(ISqlSugarClient db, InspectionResultRowSet rowSet)
    {
        db.Ado.BeginTran();
        try
        {
            db.Ado.ExecuteCommand(@"
INSERT INTO inspection_cycles (
  cycle_uuid, trigger_utc_ms, trigger_local_date, product_model, task_id, position_no,
  station_id, task_instance_id, camera_id, action_type, trigger_id, decision,
  operator_code, operator_name, summary_message, metadata_json, image_width, image_height,
  image_path, created_utc_ms, mysql_sync_status, mysql_sync_error, mysql_synced_utc_ms
) VALUES (
  @CycleUuid, @TriggerUtcMs, @TriggerLocalDate, @ProductModel, @TaskId, @PositionNo,
  @StationId, @TaskInstanceId, @CameraId, @ActionType, @TriggerId, @Decision,
  @OperatorCode, @OperatorName, @SummaryMessage, @MetadataJson, @ImageWidth, @ImageHeight,
  @ImagePath, @CreatedUtcMs, @MySqlSyncStatus, @MySqlSyncError, @MySqlSyncedUtcMs
)
ON CONFLICT(cycle_uuid) DO UPDATE SET
  trigger_utc_ms = excluded.trigger_utc_ms,
  trigger_local_date = excluded.trigger_local_date,
  product_model = excluded.product_model,
  task_id = excluded.task_id,
  position_no = excluded.position_no,
  station_id = excluded.station_id,
  task_instance_id = excluded.task_instance_id,
  camera_id = excluded.camera_id,
  action_type = excluded.action_type,
  trigger_id = excluded.trigger_id,
  decision = excluded.decision,
  operator_code = excluded.operator_code,
  operator_name = excluded.operator_name,
  summary_message = excluded.summary_message,
  metadata_json = excluded.metadata_json,
  image_width = excluded.image_width,
  image_height = excluded.image_height,
  image_path = excluded.image_path,
  mysql_sync_status = excluded.mysql_sync_status,
  mysql_sync_error = excluded.mysql_sync_error,
  mysql_synced_utc_ms = excluded.mysql_synced_utc_ms",
                ToParameters(rowSet.Cycle));

            foreach (var roi in rowSet.RoiResults)
            {
                db.Ado.ExecuteCommand(@"
INSERT INTO inspection_roi_results (
  cycle_uuid, roi_id, roi_name, model_id, decision, score, center_x, center_y,
  width, height, angle_deg, sort_order, findings_json, metrics_json,
  defect_component_count, defect_max_area_px, defect_max_perimeter_px,
  defect_max_area_perimeter_ratio, defect_summary_text, defect_components_text,
  roi_image_path
) VALUES (
  @CycleUuid, @RoiId, @RoiName, @ModelId, @Decision, @Score, @CenterX, @CenterY,
  @Width, @Height, @AngleDeg, @SortOrder, @FindingsJson, @MetricsJson,
  @DefectComponentCount, @DefectMaxAreaPx, @DefectMaxPerimeterPx,
  @DefectMaxAreaPerimeterRatio, @DefectSummaryText, @DefectComponentsText,
  @RoiImagePath
)
ON CONFLICT(cycle_uuid, roi_id) DO UPDATE SET
  roi_name = excluded.roi_name,
  model_id = excluded.model_id,
  decision = excluded.decision,
  score = excluded.score,
  center_x = excluded.center_x,
  center_y = excluded.center_y,
  width = excluded.width,
  height = excluded.height,
  angle_deg = excluded.angle_deg,
  sort_order = excluded.sort_order,
  findings_json = excluded.findings_json,
  metrics_json = excluded.metrics_json,
  defect_component_count = excluded.defect_component_count,
  defect_max_area_px = excluded.defect_max_area_px,
  defect_max_perimeter_px = excluded.defect_max_perimeter_px,
  defect_max_area_perimeter_ratio = excluded.defect_max_area_perimeter_ratio,
  defect_summary_text = excluded.defect_summary_text,
  defect_components_text = excluded.defect_components_text,
  roi_image_path = excluded.roi_image_path",
                    ToParameters(roi));
            }

            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
    }

    private static Dictionary<string, object?> ToParameters(object values)
    {
        return values
            .GetType()
            .GetProperties()
            .ToDictionary(property => property.Name, property => property.GetValue(values));
    }

    private static IReadOnlyList<InspectionRoiResultRow> ReadRois(ISqlSugarClient db, string cycleUuid)
    {
        return db.Ado.SqlQuery<InspectionRoiResultRow>(@"
SELECT
  cycle_uuid AS CycleUuid,
  roi_id AS RoiId,
  roi_name AS RoiName,
  model_id AS ModelId,
  decision AS Decision,
  score AS Score,
  center_x AS CenterX,
  center_y AS CenterY,
  width AS Width,
  height AS Height,
  angle_deg AS AngleDeg,
  sort_order AS SortOrder,
  findings_json AS FindingsJson,
  metrics_json AS MetricsJson,
  defect_component_count AS DefectComponentCount,
  defect_max_area_px AS DefectMaxAreaPx,
  defect_max_perimeter_px AS DefectMaxPerimeterPx,
  defect_max_area_perimeter_ratio AS DefectMaxAreaPerimeterRatio,
  defect_summary_text AS DefectSummaryText,
  defect_components_text AS DefectComponentsText,
  roi_image_path AS RoiImagePath
FROM inspection_roi_results
WHERE cycle_uuid = @cycle_uuid
ORDER BY sort_order, roi_id", new { cycle_uuid = cycleUuid });
    }
}
