using MySqlConnector;

namespace VideoInferenceDemo;

public sealed class MySqlInspectionResultRepository
{
    private readonly string _connectionString;
    private bool _schemaEnsured;
    private readonly SemaphoreSlim _schemaGate = new(1, 1);

    public MySqlInspectionResultRepository(string connectionString)
    {
        _connectionString = string.IsNullOrWhiteSpace(connectionString)
            ? throw new ArgumentException("MySQL connection string is required.", nameof(connectionString))
            : connectionString.Trim();
    }

    public async Task WriteAsync(InspectionResultStorageItem item, CancellationToken cancellationToken = default)
    {
        var rowSet = InspectionResultRowMapper.Map(item, InspectionResultMySqlSyncStatus.Synced);
        await WriteAsync(rowSet, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteAsync(InspectionResultRowSet rowSet, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
        rowSet.Cycle.MySqlSyncStatus = InspectionResultMySqlSyncStatus.Synced.ToString();
        rowSet.Cycle.MySqlSyncError = null;
        rowSet.Cycle.MySqlSyncedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ExecuteAsync(connection, transaction, @"
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
ON DUPLICATE KEY UPDATE
  trigger_utc_ms = VALUES(trigger_utc_ms),
  trigger_local_date = VALUES(trigger_local_date),
  product_model = VALUES(product_model),
  task_id = VALUES(task_id),
  position_no = VALUES(position_no),
  station_id = VALUES(station_id),
  task_instance_id = VALUES(task_instance_id),
  camera_id = VALUES(camera_id),
  action_type = VALUES(action_type),
  trigger_id = VALUES(trigger_id),
  decision = VALUES(decision),
  operator_code = VALUES(operator_code),
  operator_name = VALUES(operator_name),
  summary_message = VALUES(summary_message),
  metadata_json = VALUES(metadata_json),
  image_width = VALUES(image_width),
  image_height = VALUES(image_height),
  image_path = VALUES(image_path),
  mysql_sync_status = VALUES(mysql_sync_status),
  mysql_sync_error = VALUES(mysql_sync_error),
  mysql_synced_utc_ms = VALUES(mysql_synced_utc_ms)",
                rowSet.Cycle,
                cancellationToken).ConfigureAwait(false);

            foreach (var roi in rowSet.RoiResults)
            {
                await ExecuteAsync(connection, transaction, @"
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
ON DUPLICATE KEY UPDATE
  roi_name = VALUES(roi_name),
  model_id = VALUES(model_id),
  decision = VALUES(decision),
  score = VALUES(score),
  center_x = VALUES(center_x),
  center_y = VALUES(center_y),
  width = VALUES(width),
  height = VALUES(height),
  angle_deg = VALUES(angle_deg),
  sort_order = VALUES(sort_order),
  findings_json = VALUES(findings_json),
  metrics_json = VALUES(metrics_json),
  defect_component_count = VALUES(defect_component_count),
  defect_max_area_px = VALUES(defect_max_area_px),
  defect_max_perimeter_px = VALUES(defect_max_perimeter_px),
  defect_max_area_perimeter_ratio = VALUES(defect_max_area_perimeter_ratio),
  defect_summary_text = VALUES(defect_summary_text),
  defect_components_text = VALUES(defect_components_text),
  roi_image_path = VALUES(roi_image_path)",
                    roi,
                    cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaEnsured)
        {
            return;
        }

        await _schemaGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_schemaEnsured)
            {
                return;
            }

            await EnsureDatabaseAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            foreach (var sql in SchemaStatements)
            {
                await using var command = new MySqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await EnsureInspectionRoiResultColumnsAsync(connection, cancellationToken).ConfigureAwait(false);

            _schemaEnsured = true;
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    private async Task EnsureDatabaseAsync(CancellationToken cancellationToken)
    {
        var builder = new MySqlConnectionStringBuilder(_connectionString);
        var database = builder.Database;
        if (string.IsNullOrWhiteSpace(database))
        {
            return;
        }

        builder.Database = string.Empty;
        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var escapedDatabase = database.Replace("`", "``", StringComparison.Ordinal);
        await using var command = new MySqlCommand(
            $"CREATE DATABASE IF NOT EXISTS `{escapedDatabase}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",
            connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string sql,
        object values,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(sql, connection, transaction);
        foreach (var property in values.GetType().GetProperties())
        {
            command.Parameters.AddWithValue("@" + property.Name, property.GetValue(values) ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureInspectionRoiResultColumnsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var database = new MySqlConnectionStringBuilder(_connectionString).Database;
        if (string.IsNullOrWhiteSpace(database))
        {
            return;
        }

        var columns = new (string Name, string Definition)[]
        {
            ("defect_component_count", "INT NULL"),
            ("defect_max_area_px", "DOUBLE NULL"),
            ("defect_max_perimeter_px", "DOUBLE NULL"),
            ("defect_max_area_perimeter_ratio", "DOUBLE NULL"),
            ("defect_summary_text", "TEXT NULL"),
            ("defect_components_text", "TEXT NULL")
        };

        foreach (var (name, definition) in columns)
        {
            if (await ColumnExistsAsync(connection, database, "inspection_roi_results", name, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            await using var alter = new MySqlCommand($"ALTER TABLE inspection_roi_results ADD COLUMN {name} {definition}", connection);
            await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<bool> ColumnExistsAsync(
        MySqlConnection connection,
        string database,
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(@"
SELECT COUNT(*)
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = @schema
  AND TABLE_NAME = @table
  AND COLUMN_NAME = @column", connection);
        command.Parameters.AddWithValue("@schema", database);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        return count > 0;
    }

    private static readonly string[] SchemaStatements =
    [
        @"
CREATE TABLE IF NOT EXISTS inspection_cycles (
  cycle_uuid VARCHAR(64) PRIMARY KEY,
  trigger_utc_ms BIGINT NOT NULL,
  trigger_local_date VARCHAR(10) NOT NULL,
  product_model VARCHAR(128) NOT NULL,
  task_id VARCHAR(128) NOT NULL,
  position_no VARCHAR(128) NOT NULL,
  station_id VARCHAR(128) NULL,
  task_instance_id VARCHAR(128) NULL,
  camera_id VARCHAR(128) NULL,
  action_type VARCHAR(128) NULL,
  trigger_id VARCHAR(128) NULL,
  decision VARCHAR(32) NOT NULL,
  operator_code VARCHAR(128) NULL,
  operator_name VARCHAR(128) NULL,
  summary_message TEXT NULL,
  metadata_json JSON NULL,
  image_width INT NOT NULL DEFAULT 0,
  image_height INT NOT NULL DEFAULT 0,
  image_path TEXT NULL,
  created_utc_ms BIGINT NOT NULL,
  mysql_sync_status VARCHAR(32) NOT NULL DEFAULT 'None',
  mysql_sync_error TEXT NULL,
  mysql_synced_utc_ms BIGINT NULL,
  KEY idx_inspection_cycles_time (trigger_utc_ms),
  KEY idx_inspection_cycles_task_time (task_id, trigger_utc_ms),
  KEY idx_inspection_cycles_camera_time (camera_id, trigger_utc_ms),
  KEY idx_inspection_cycles_decision_time (decision, trigger_utc_ms),
  KEY idx_inspection_cycles_operator_time (operator_code, trigger_utc_ms)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
        @"
CREATE TABLE IF NOT EXISTS inspection_roi_results (
  id BIGINT AUTO_INCREMENT PRIMARY KEY,
  cycle_uuid VARCHAR(64) NOT NULL,
  roi_id VARCHAR(128) NOT NULL,
  roi_name VARCHAR(128) NULL,
  model_id VARCHAR(128) NULL,
  decision VARCHAR(32) NOT NULL,
  score DOUBLE NULL,
  center_x DOUBLE NOT NULL,
  center_y DOUBLE NOT NULL,
  width DOUBLE NOT NULL,
  height DOUBLE NOT NULL,
  angle_deg DOUBLE NOT NULL,
  sort_order INT NOT NULL DEFAULT 0,
  findings_json JSON NULL,
  metrics_json JSON NULL,
  defect_component_count INT NULL,
  defect_max_area_px DOUBLE NULL,
  defect_max_perimeter_px DOUBLE NULL,
  defect_max_area_perimeter_ratio DOUBLE NULL,
  defect_summary_text TEXT NULL,
  defect_components_text TEXT NULL,
  roi_image_path TEXT NULL,
  UNIQUE KEY ux_inspection_roi_cycle_roi (cycle_uuid, roi_id),
  KEY idx_inspection_roi_cycle (cycle_uuid),
  CONSTRAINT fk_inspection_roi_cycle FOREIGN KEY (cycle_uuid) REFERENCES inspection_cycles(cycle_uuid) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
    ];
}
