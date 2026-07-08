using SqlSugar;

namespace VideoInferenceDemo;

public static class SqliteResultSchema
{
    public static void Ensure(ISqlSugarClient db)
    {
        db.Ado.ExecuteCommand(@"
CREATE TABLE IF NOT EXISTS sources (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  source_key TEXT NOT NULL UNIQUE,
  source_type TEXT,
  width_px INTEGER,
  height_px INTEGER,
  fps REAL,
  duration_ms INTEGER,
  created_utc_ms INTEGER NOT NULL,
  updated_utc_ms INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS models (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  model_key TEXT NOT NULL UNIQUE,
  model_path TEXT,
  task_type TEXT NOT NULL CHECK (task_type IN ('det')),
  created_utc_ms INTEGER NOT NULL,
  updated_utc_ms INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS inference_runs (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  run_uuid TEXT NOT NULL UNIQUE,
  source_id INTEGER NOT NULL,
  started_utc_ms INTEGER NOT NULL,
  ended_utc_ms INTEGER,
  status TEXT NOT NULL CHECK (status IN ('running', 'completed', 'failed', 'stopped')),
  app_version TEXT,
  host_name TEXT,
  config_json TEXT,
  notes TEXT,
  FOREIGN KEY (source_id) REFERENCES sources(id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS run_models (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  run_id INTEGER NOT NULL,
  model_id INTEGER NOT NULL,
  role TEXT NOT NULL DEFAULT 'primary',
  created_utc_ms INTEGER NOT NULL,
  UNIQUE (run_id, model_id),
  FOREIGN KEY (run_id) REFERENCES inference_runs(id) ON DELETE CASCADE,
  FOREIGN KEY (model_id) REFERENCES models(id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS raw_det (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  run_id INTEGER NOT NULL,
  model_id INTEGER NOT NULL,
  frame_index INTEGER NOT NULL,
  class_id INTEGER NOT NULL,
  score_q1000 INTEGER NOT NULL,
  x_px INTEGER NOT NULL,
  y_px INTEGER NOT NULL,
  w_px INTEGER NOT NULL,
  h_px INTEGER NOT NULL,
  CHECK (score_q1000 BETWEEN 0 AND 1000),
  CHECK (x_px >= 0 AND y_px >= 0 AND w_px >= 0 AND h_px >= 0)
);

CREATE TABLE IF NOT EXISTS tcn_feature_versions (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  version TEXT NOT NULL,
  feature_dim INTEGER NOT NULL,
  config_json TEXT,
  created_utc_ms INTEGER NOT NULL,
  UNIQUE (name, version)
);

CREATE TABLE IF NOT EXISTS tcn_features (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  run_id INTEGER NOT NULL,
  frame_index INTEGER NOT NULL,
  pts_ms INTEGER NOT NULL,
  feature_version_id INTEGER NOT NULL,
  feature_blob BLOB NOT NULL,
  created_utc_ms INTEGER NOT NULL,
  UNIQUE (run_id, frame_index, feature_version_id),
  FOREIGN KEY (run_id) REFERENCES inference_runs(id) ON DELETE CASCADE,
  FOREIGN KEY (feature_version_id) REFERENCES tcn_feature_versions(id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS fsm_labels (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  run_id INTEGER NOT NULL,
  step_index INTEGER NOT NULL DEFAULT -1,
  label TEXT NOT NULL,
  source_type TEXT NOT NULL,
  score_q INTEGER,
  start_pts_ms INTEGER NOT NULL,
  end_pts_ms INTEGER NOT NULL,
  start_utc_ms INTEGER NOT NULL,
  end_utc_ms INTEGER NOT NULL,
  created_utc_ms INTEGER NOT NULL,
  updated_utc_ms INTEGER NOT NULL,
  CHECK (end_pts_ms > start_pts_ms),
  CHECK (end_utc_ms > start_utc_ms),
  CHECK (score_q IS NULL OR (score_q BETWEEN 0 AND 10000)),
  UNIQUE (run_id, source_type, step_index, label, start_utc_ms, end_utc_ms),
  FOREIGN KEY (run_id) REFERENCES inference_runs(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS analysis_results (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  run_id INTEGER NOT NULL,
  strategy_name TEXT NOT NULL,
  task_id TEXT,
  frame_index INTEGER NOT NULL,
  pts_ms INTEGER NOT NULL,
  frame_utc_ms INTEGER NOT NULL,
  step INTEGER,
  label TEXT,
  score_q1000 INTEGER,
  expected_state_code TEXT,
  current_state_code TEXT,
  ng_reason TEXT,
  debug_note TEXT,
  is_transition INTEGER NOT NULL DEFAULT 0 CHECK (is_transition IN (0, 1)),
  is_reset INTEGER NOT NULL DEFAULT 0 CHECK (is_reset IN (0, 1)),
  transition_ok INTEGER CHECK (transition_ok IS NULL OR transition_ok IN (0, 1)),
  from_step INTEGER,
  to_step INTEGER,
  created_utc_ms INTEGER NOT NULL,
  UNIQUE (run_id, strategy_name, frame_index),
  FOREIGN KEY (run_id) REFERENCES inference_runs(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS run_operator_assignments (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  run_uuid TEXT NOT NULL UNIQUE,
  employee_code TEXT NOT NULL,
  employee_name TEXT,
  employee_team TEXT,
  session_name TEXT,
  camera_id TEXT,
  assigned_utc_ms INTEGER NOT NULL,
  released_utc_ms INTEGER,
  note TEXT
);

CREATE TABLE IF NOT EXISTS run_production_stats (
  run_uuid TEXT PRIMARY KEY,
  ok_count INTEGER NOT NULL DEFAULT 0,
  ng_count INTEGER NOT NULL DEFAULT 0,
  updated_utc_ms INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS sop_alarm_events (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  event_uuid TEXT NOT NULL UNIQUE,
  event_type TEXT NOT NULL CHECK (event_type IN ('alarm', 'reset')),
  run_uuid TEXT,
  camera_id TEXT,
  session_name TEXT,
  source_key TEXT,
  step INTEGER,
  ng_reason TEXT,
  reset_source TEXT,
  fingerprint_module_id TEXT,
  fingerprint_module_name TEXT,
  fingerprint_id INTEGER CHECK (fingerprint_id IS NULL OR (fingerprint_id BETWEEN 1 AND 255)),
  employee_code TEXT,
  employee_name TEXT,
  event_utc_ms INTEGER NOT NULL,
  related_alarm_event_uuid TEXT,
  note TEXT
);

CREATE TABLE IF NOT EXISTS inspection_cycles (
  cycle_uuid TEXT PRIMARY KEY,
  trigger_utc_ms INTEGER NOT NULL,
  trigger_local_date TEXT NOT NULL,
  product_model TEXT NOT NULL,
  task_id TEXT NOT NULL,
  position_no TEXT NOT NULL,
  station_id TEXT,
  task_instance_id TEXT,
  camera_id TEXT,
  action_type TEXT,
  trigger_id TEXT,
  decision TEXT NOT NULL,
  operator_code TEXT,
  operator_name TEXT,
  summary_message TEXT,
  metadata_json TEXT,
  image_width INTEGER NOT NULL DEFAULT 0,
  image_height INTEGER NOT NULL DEFAULT 0,
  image_path TEXT,
  created_utc_ms INTEGER NOT NULL,
  mysql_sync_status TEXT NOT NULL DEFAULT 'None',
  mysql_sync_error TEXT,
  mysql_synced_utc_ms INTEGER
);

CREATE TABLE IF NOT EXISTS inspection_roi_results (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  cycle_uuid TEXT NOT NULL,
  roi_id TEXT NOT NULL,
  roi_name TEXT,
  model_id TEXT,
  decision TEXT NOT NULL,
  score REAL,
  center_x REAL NOT NULL,
  center_y REAL NOT NULL,
  width REAL NOT NULL,
  height REAL NOT NULL,
  angle_deg REAL NOT NULL,
  sort_order INTEGER NOT NULL DEFAULT 0,
  findings_json TEXT,
  metrics_json TEXT,
  defect_component_count INTEGER,
  defect_max_area_px REAL,
  defect_max_perimeter_px REAL,
  defect_max_area_perimeter_ratio REAL,
  defect_summary_text TEXT,
  defect_components_text TEXT,
  roi_image_path TEXT,
  UNIQUE (cycle_uuid, roi_id),
  FOREIGN KEY (cycle_uuid) REFERENCES inspection_cycles(cycle_uuid) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_runs_source_started ON inference_runs(source_id, started_utc_ms DESC);
CREATE INDEX IF NOT EXISTS idx_runs_status_started ON inference_runs(status, started_utc_ms DESC);
CREATE INDEX IF NOT EXISTS idx_run_models_run ON run_models(run_id);
CREATE INDEX IF NOT EXISTS idx_run_models_model ON run_models(model_id);
CREATE INDEX IF NOT EXISTS idx_raw_det_run_frame ON raw_det(run_id, model_id, frame_index);
CREATE INDEX IF NOT EXISTS idx_raw_det_run_class ON raw_det(run_id, class_id);
CREATE INDEX IF NOT EXISTS idx_tcn_features_run_frame ON tcn_features(run_id, frame_index);
CREATE INDEX IF NOT EXISTS idx_tcn_features_version ON tcn_features(feature_version_id);
CREATE INDEX IF NOT EXISTS idx_fsm_labels_run_utc ON fsm_labels(run_id, start_utc_ms, end_utc_ms);
CREATE INDEX IF NOT EXISTS idx_fsm_labels_run_pts ON fsm_labels(run_id, start_pts_ms, end_pts_ms);
CREATE INDEX IF NOT EXISTS idx_fsm_labels_label ON fsm_labels(label);
CREATE INDEX IF NOT EXISTS idx_analysis_results_run_frame ON analysis_results(run_id, frame_index);
CREATE INDEX IF NOT EXISTS idx_analysis_results_run_pts ON analysis_results(run_id, pts_ms);
CREATE INDEX IF NOT EXISTS idx_analysis_results_run_transition ON analysis_results(run_id, is_transition, frame_index);
CREATE INDEX IF NOT EXISTS idx_assign_emp_time ON run_operator_assignments(employee_code, assigned_utc_ms DESC);
CREATE INDEX IF NOT EXISTS idx_assign_time ON run_operator_assignments(assigned_utc_ms DESC);
CREATE INDEX IF NOT EXISTS idx_assign_camera_time ON run_operator_assignments(camera_id, assigned_utc_ms DESC);
CREATE INDEX IF NOT EXISTS idx_run_stats_time ON run_production_stats(updated_utc_ms DESC);
CREATE INDEX IF NOT EXISTS idx_sop_alarm_events_run ON sop_alarm_events(run_uuid, event_utc_ms);
CREATE INDEX IF NOT EXISTS idx_sop_alarm_events_type_time ON sop_alarm_events(event_type, event_utc_ms DESC);
CREATE INDEX IF NOT EXISTS idx_sop_alarm_events_camera_time ON sop_alarm_events(camera_id, event_utc_ms DESC);
CREATE INDEX IF NOT EXISTS idx_inspection_cycles_time ON inspection_cycles(trigger_utc_ms DESC);
CREATE INDEX IF NOT EXISTS idx_inspection_cycles_task_time ON inspection_cycles(task_id, trigger_utc_ms DESC);
CREATE INDEX IF NOT EXISTS idx_inspection_cycles_camera_time ON inspection_cycles(camera_id, trigger_utc_ms DESC);
CREATE INDEX IF NOT EXISTS idx_inspection_cycles_decision_time ON inspection_cycles(decision, trigger_utc_ms DESC);
CREATE INDEX IF NOT EXISTS idx_inspection_cycles_operator_time ON inspection_cycles(operator_code, trigger_utc_ms DESC);
CREATE INDEX IF NOT EXISTS idx_inspection_cycles_sync ON inspection_cycles(mysql_sync_status, trigger_utc_ms);
CREATE INDEX IF NOT EXISTS idx_inspection_roi_cycle ON inspection_roi_results(cycle_uuid);
");
        EnsureViews(db);
        MigrateResultSchemaV1(db);
    }

    public static void EnsureViews(ISqlSugarClient db)
    {
        db.Ado.ExecuteCommand(@"
DROP VIEW IF EXISTS v_run_summary;
DROP VIEW IF EXISTS v_fsm_dist;
DROP VIEW IF EXISTS v_operator_kpi_daily;

CREATE VIEW v_run_summary AS
SELECT
  r.id AS run_id,
  r.run_uuid,
  r.status,
  r.started_utc_ms,
  r.ended_utc_ms,
  datetime(r.started_utc_ms / 1000, 'unixepoch', 'localtime') AS started_utc,
  datetime(r.ended_utc_ms / 1000, 'unixepoch', 'localtime') AS ended_utc,
  CASE WHEN r.ended_utc_ms IS NULL THEN NULL ELSE r.ended_utc_ms - r.started_utc_ms END AS duration_ms,
  r.source_id,
  s.source_key
FROM inference_runs r
JOIN sources s ON s.id = r.source_id;");
    }

    private static void MigrateResultSchemaV1(ISqlSugarClient db)
    {
        AddColumnIfMissing(db, "run_operator_assignments", "employee_name", "TEXT");
        AddColumnIfMissing(db, "run_operator_assignments", "employee_team", "TEXT");
        AddColumnIfMissing(db, "inspection_roi_results", "defect_component_count", "INTEGER");
        AddColumnIfMissing(db, "inspection_roi_results", "defect_max_area_px", "REAL");
        AddColumnIfMissing(db, "inspection_roi_results", "defect_max_perimeter_px", "REAL");
        AddColumnIfMissing(db, "inspection_roi_results", "defect_max_area_perimeter_ratio", "REAL");
        AddColumnIfMissing(db, "inspection_roi_results", "defect_summary_text", "TEXT");
        AddColumnIfMissing(db, "inspection_roi_results", "defect_components_text", "TEXT");
    }

    private static void AddColumnIfMissing(ISqlSugarClient db, string tableName, string columnName, string definition)
    {
        var existingColumns = db.Ado.SqlQuery<string>(
            $"SELECT name FROM pragma_table_info('{tableName}') WHERE name = @column_name",
            new { column_name = columnName });
        if (existingColumns.Count != 0)
        {
            return;
        }

        db.Ado.ExecuteCommand($"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition}");
    }
}
