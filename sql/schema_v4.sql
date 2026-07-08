-- v4 schema (minimal, q1000 features only)
PRAGMA foreign_keys = ON;

DROP VIEW IF EXISTS v_run_summary;
DROP VIEW IF EXISTS v_fsm_dist;

DROP TABLE IF EXISTS fsm_labels;
DROP TABLE IF EXISTS tcn_features;
DROP TABLE IF EXISTS tcn_feature_versions;
DROP TABLE IF EXISTS fsm_frame_features;
DROP TABLE IF EXISTS raw_det;
DROP TABLE IF EXISTS raw_frame_meta;
DROP TABLE IF EXISTS run_models;
DROP TABLE IF EXISTS inference_runs;
DROP TABLE IF EXISTS models;
DROP TABLE IF EXISTS sources;

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

CREATE TABLE IF NOT EXISTS fsm_frame_features (
  run_id INTEGER NOT NULL,
  model_id INTEGER NOT NULL,
  frame_index INTEGER NOT NULL,
  pts_ms INTEGER NOT NULL,
  frame_utc_ms INTEGER NOT NULL,
  center_score_q1000 INTEGER,
  score_id0_q1000 INTEGER,
  score_id1_q1000 INTEGER,
  dist_id0_to_id2_q1000 INTEGER NOT NULL,
  dist_id1_to_id2_q1000 INTEGER NOT NULL,
  area_id0_px INTEGER,
  area_id1_px INTEGER,
  area_id2_px INTEGER,
  created_utc_ms INTEGER NOT NULL,
  PRIMARY KEY (run_id, model_id, frame_index),
  CHECK (center_score_q1000 IS NULL OR (center_score_q1000 BETWEEN 0 AND 1000)),
  CHECK (score_id0_q1000 IS NULL OR (score_id0_q1000 BETWEEN 0 AND 1000)),
  CHECK (score_id1_q1000 IS NULL OR (score_id1_q1000 BETWEEN 0 AND 1000)),
  CHECK (dist_id0_to_id2_q1000 BETWEEN 0 AND 65535),
  CHECK (dist_id1_to_id2_q1000 BETWEEN 0 AND 65535),
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
  CHECK (x_px >= 0 AND y_px >= 0 AND w_px >= 0 AND h_px >= 0),
  FOREIGN KEY (run_id, model_id, frame_index) REFERENCES fsm_frame_features(run_id, model_id, frame_index) ON DELETE CASCADE
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

CREATE INDEX IF NOT EXISTS idx_runs_source_started ON inference_runs(source_id, started_utc_ms DESC);
CREATE INDEX IF NOT EXISTS idx_runs_status_started ON inference_runs(status, started_utc_ms DESC);
CREATE INDEX IF NOT EXISTS idx_run_models_run ON run_models(run_id);
CREATE INDEX IF NOT EXISTS idx_run_models_model ON run_models(model_id);
CREATE INDEX IF NOT EXISTS idx_fsm_features_run_frame ON fsm_frame_features(run_id, frame_index);
CREATE INDEX IF NOT EXISTS idx_fsm_features_run_pts ON fsm_frame_features(run_id, pts_ms);
CREATE INDEX IF NOT EXISTS idx_fsm_features_run_utc ON fsm_frame_features(run_id, frame_utc_ms);
CREATE INDEX IF NOT EXISTS idx_raw_det_run_frame ON raw_det(run_id, model_id, frame_index);
CREATE INDEX IF NOT EXISTS idx_raw_det_run_class ON raw_det(run_id, class_id);
CREATE INDEX IF NOT EXISTS idx_tcn_features_run_frame ON tcn_features(run_id, frame_index);
CREATE INDEX IF NOT EXISTS idx_tcn_features_version ON tcn_features(feature_version_id);
CREATE INDEX IF NOT EXISTS idx_fsm_labels_run_utc ON fsm_labels(run_id, start_utc_ms, end_utc_ms);
CREATE INDEX IF NOT EXISTS idx_fsm_labels_run_pts ON fsm_labels(run_id, start_pts_ms, end_pts_ms);
CREATE INDEX IF NOT EXISTS idx_fsm_labels_label ON fsm_labels(label);


DROP VIEW IF EXISTS v_run_summary;
DROP VIEW IF EXISTS v_fsm_dist;

CREATE VIEW v_run_summary AS
SELECT
  r.id AS run_id,
  r.run_uuid,
  r.status,
  r.started_utc_ms,
  r.ended_utc_ms,
  CASE WHEN r.ended_utc_ms IS NULL THEN NULL ELSE r.ended_utc_ms - r.started_utc_ms END AS duration_ms,
  r.source_id,
  s.source_key,
  COUNT(f.frame_index) AS frame_count,
  MIN(f.pts_ms) AS min_pts_ms,
  MAX(f.pts_ms) AS max_pts_ms,
  MIN(f.frame_utc_ms) AS min_frame_utc_ms,
  MAX(f.frame_utc_ms) AS max_frame_utc_ms
FROM inference_runs r
JOIN sources s ON s.id = r.source_id
LEFT JOIN fsm_frame_features f ON f.run_id = r.id
GROUP BY
  r.id,
  r.run_uuid,
  r.status,
  r.started_utc_ms,
  r.ended_utc_ms,
  r.source_id,
  s.source_key;

CREATE VIEW v_fsm_dist AS
SELECT
  f.run_id,
  r.source_id,
  s.source_key,
  f.model_id,
  f.frame_index,
  f.pts_ms,
  f.frame_utc_ms,
  2 AS center_class_id,
  f.center_score_q1000,
  f.score_id0_q1000,
  f.score_id1_q1000,
  f.dist_id0_to_id2_q1000,
  f.dist_id1_to_id2_q1000,
  f.area_id0_px,
  f.area_id1_px,
  f.area_id2_px
FROM fsm_frame_features f
JOIN inference_runs r ON r.id = f.run_id
JOIN sources s ON s.id = r.source_id;

