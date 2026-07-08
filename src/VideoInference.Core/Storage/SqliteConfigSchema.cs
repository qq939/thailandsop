using SqlSugar;

namespace VideoInferenceDemo;

public static class SqliteConfigSchema
{
    public static void Ensure(ISqlSugarClient db)
    {
        db.Ado.ExecuteCommand(@"
CREATE TABLE IF NOT EXISTS personnel (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  employee_code TEXT NOT NULL UNIQUE,
  employee_name TEXT NOT NULL,
  password_text TEXT NOT NULL DEFAULT '',
  team TEXT,
  fingerprint_id INTEGER CHECK (fingerprint_id IS NULL OR (fingerprint_id BETWEEN 1 AND 255)),
  is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
  note TEXT,
  created_utc_ms INTEGER NOT NULL,
  updated_utc_ms INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS personnel_fingerprint_bindings (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  employee_code TEXT NOT NULL,
  fingerprint_module_id TEXT NOT NULL,
  fingerprint_id INTEGER NOT NULL CHECK (fingerprint_id BETWEEN 1 AND 255),
  created_utc_ms INTEGER NOT NULL,
  updated_utc_ms INTEGER NOT NULL,
  UNIQUE (employee_code, fingerprint_module_id),
  UNIQUE (fingerprint_module_id, fingerprint_id),
  FOREIGN KEY (employee_code) REFERENCES personnel(employee_code) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS camera_profiles (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
  auto_start INTEGER NOT NULL CHECK (auto_start IN (0, 1)),
  provider_id TEXT NOT NULL,
  camera_index INTEGER NOT NULL,
  device_id TEXT NOT NULL,
  opencv_source TEXT NOT NULL,
  opencv_backend TEXT NOT NULL,
  trigger_mode TEXT NOT NULL,
  rotation TEXT NOT NULL,
  mirror_mode TEXT NOT NULL,
  target_fps REAL NOT NULL,
  use_source_pts_for_video INTEGER NOT NULL CHECK (use_source_pts_for_video IN (0, 1)),
  primary_task_id TEXT NOT NULL,
  selected_sop_profile_id TEXT NOT NULL,
  enable_sop_analysis INTEGER NOT NULL CHECK (enable_sop_analysis IN (0, 1)),
  analysis_frame_window_size INTEGER NOT NULL,
  analysis_state_window_size INTEGER NOT NULL,
  analysis_hold_frames INTEGER NOT NULL,
  sop_window_ms INTEGER NOT NULL,
  sop_min_score_q1000 INTEGER NOT NULL,
  sop_min_visible_ratio_q1000 INTEGER NOT NULL,
  ocr_enabled INTEGER NOT NULL DEFAULT 0 CHECK (ocr_enabled IN (0, 1)),
  ocr_roi_x INTEGER NOT NULL DEFAULT 0,
  ocr_roi_y INTEGER NOT NULL DEFAULT 0,
  ocr_roi_width INTEGER NOT NULL DEFAULT 200,
  ocr_roi_height INTEGER NOT NULL DEFAULT 40,
  enable_camera_recording INTEGER NOT NULL CHECK (enable_camera_recording IN (0, 1)),
  recording_root_directory TEXT NOT NULL,
  recording_segment_minutes INTEGER NOT NULL,
  recording_container_format TEXT NOT NULL,
  recording_video_encoder TEXT NOT NULL,
  recording_codec_fourcc TEXT NOT NULL,
  recording_queue_capacity INTEGER NOT NULL,
  recording_bitrate_mbps INTEGER NOT NULL,
  recording_fps REAL NOT NULL,
  sort_order INTEGER NOT NULL,
  created_utc_ms INTEGER NOT NULL,
  updated_utc_ms INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS camera_settings_state (
  key TEXT PRIMARY KEY,
  value TEXT NOT NULL,
  updated_utc_ms INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS sop_profiles (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  strategy TEXT NOT NULL,
  fingerprint_module_id TEXT NOT NULL,
  sort_order INTEGER NOT NULL,
  created_utc_ms INTEGER NOT NULL,
  updated_utc_ms INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS sop_steps (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  profile_id TEXT NOT NULL,
  step INTEGER NOT NULL,
  name TEXT NOT NULL,
  action_code TEXT,
  tcn_label TEXT,
  expected_state_code TEXT,
  created_utc_ms INTEGER NOT NULL,
  updated_utc_ms INTEGER NOT NULL,
  UNIQUE (profile_id, step),
  FOREIGN KEY (profile_id) REFERENCES sop_profiles(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS fingerprint_modules (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
  connection_kind TEXT NOT NULL,
  slave_address INTEGER NOT NULL,
  port_name TEXT NOT NULL,
  baud_rate INTEGER NOT NULL,
  data_bits INTEGER NOT NULL,
  parity TEXT NOT NULL,
  stop_bits INTEGER NOT NULL,
  host TEXT NOT NULL,
  tcp_port INTEGER NOT NULL,
  read_timeout_ms INTEGER NOT NULL,
  write_timeout_ms INTEGER NOT NULL,
  poll_interval_ms INTEGER NOT NULL,
  duplicate_suppress_ms INTEGER NOT NULL,
  sort_order INTEGER NOT NULL,
  created_utc_ms INTEGER NOT NULL,
  updated_utc_ms INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS modbus_modules (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  enabled INTEGER NOT NULL CHECK (enabled IN (0, 1)),
  host TEXT NOT NULL,
  port INTEGER NOT NULL,
  slave_address INTEGER NOT NULL,
  poll_interval_ms INTEGER NOT NULL,
  output_start_address INTEGER NOT NULL,
  input_start_address INTEGER NOT NULL,
  sort_order INTEGER NOT NULL,
  created_utc_ms INTEGER NOT NULL,
  updated_utc_ms INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS three_color_light_bindings (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  module_id TEXT NOT NULL,
  light_number INTEGER NOT NULL,
  red_channel_number INTEGER NOT NULL,
  green_channel_number INTEGER NOT NULL,
  buzzer_channel_number INTEGER NOT NULL,
  buzzer_enabled INTEGER NOT NULL CHECK (buzzer_enabled IN (0, 1)),
  sort_order INTEGER NOT NULL,
  created_utc_ms INTEGER NOT NULL,
  updated_utc_ms INTEGER NOT NULL,
  UNIQUE (module_id, light_number),
  FOREIGN KEY (module_id) REFERENCES modbus_modules(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_personnel_active ON personnel(is_active, employee_code);
CREATE INDEX IF NOT EXISTS idx_fp_bindings_emp ON personnel_fingerprint_bindings(employee_code);
CREATE INDEX IF NOT EXISTS idx_fp_bindings_module ON personnel_fingerprint_bindings(fingerprint_module_id);
CREATE INDEX IF NOT EXISTS idx_fp_bindings_lookup ON personnel_fingerprint_bindings(fingerprint_module_id, fingerprint_id);
CREATE INDEX IF NOT EXISTS idx_camera_profiles_order ON camera_profiles(sort_order);
CREATE INDEX IF NOT EXISTS idx_sop_profiles_order ON sop_profiles(sort_order);
CREATE INDEX IF NOT EXISTS idx_sop_steps_profile_step ON sop_steps(profile_id, step);
CREATE INDEX IF NOT EXISTS idx_fingerprint_modules_order ON fingerprint_modules(sort_order);
CREATE INDEX IF NOT EXISTS idx_modbus_modules_order ON modbus_modules(sort_order);
CREATE INDEX IF NOT EXISTS idx_three_color_light_bindings_module ON three_color_light_bindings(module_id, sort_order);
");
        MigratePersonnelV1(db);
        MigrateCameraProfileV1(db);
    }

    private static void MigratePersonnelV1(ISqlSugarClient db)
    {
        AddColumnIfMissing(db, "personnel", "password_text", "TEXT NOT NULL DEFAULT ''");
    }

    private static void AddColumnIfMissing(ISqlSugarClient db, string tableName, string columnName, string definition)
    {
        var existingColumns = db.Ado.SqlQuery<string>(
            $"SELECT name FROM pragma_table_info('{tableName}') WHERE name = @name",
            new { name = columnName });
        if (existingColumns.Count != 0)
        {
            return;
        }

        db.Ado.ExecuteCommand($"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition}");
    }

    private static void MigrateCameraProfileV1(ISqlSugarClient db)
    {
        var existingColumns = db.Ado.SqlQuery<string>(
            "SELECT name FROM pragma_table_info('camera_profiles') WHERE name = 'ocr_enabled'");
        if (existingColumns.Count != 0)
        {
            return;
        }

        db.Ado.ExecuteCommand("ALTER TABLE camera_profiles ADD COLUMN ocr_enabled INTEGER NOT NULL DEFAULT 0 CHECK (ocr_enabled IN (0, 1))");
        db.Ado.ExecuteCommand("ALTER TABLE camera_profiles ADD COLUMN ocr_roi_x INTEGER NOT NULL DEFAULT 0");
        db.Ado.ExecuteCommand("ALTER TABLE camera_profiles ADD COLUMN ocr_roi_y INTEGER NOT NULL DEFAULT 0");
        db.Ado.ExecuteCommand("ALTER TABLE camera_profiles ADD COLUMN ocr_roi_width INTEGER NOT NULL DEFAULT 200");
        db.Ado.ExecuteCommand("ALTER TABLE camera_profiles ADD COLUMN ocr_roi_height INTEGER NOT NULL DEFAULT 40");
    }
}
