# DATABASE v4 Notes

This document describes the target schema in `sql/schema_v4.sql` and retention templates in `sql/retention_v4.sql`.

## Design goals

1. Run-first data model: each inference execution has an `inference_runs.id`.
2. No cross-run collision: all queries are scoped by `run_id`.
3. Compact result storage: store only per-frame FSM features in `q1000` integers.
4. Minimize write and query cost by avoiding per-detection rows.
5. Optional raw per-detection storage is gated by `db_config.json` for debugging.

## Key changes

1. `source_id + pts_ms` is no longer a dedup key across the whole DB.
2. Relative timeline (`pts_ms`) and absolute timeline (`*_utc_ms`) are both stored.
3. Labels use UTC + run for idempotency:
   - unique(run_id, source_type, step_index, label, start_utc_ms, end_utc_ms)
4. Curve views (`v_curve_*`) are omitted in the minimal schema to reduce storage and compute.

## Coordinate and score encoding

1. Distances are stored as integer `q1000` (`0..1000`) in `fsm_frame_features`.
2. Scores are stored as integer `q1000` (`0..1000`) in `fsm_frame_features`.
3. Areas are stored as pixel integers (`area_id*_px`).

## Optional raw detections

When `db_config.json` sets `EnableRawDetections` to `true`, the writer also saves:

- `raw_det`: one row per detected object per frame (`x_px`, `y_px`, `w_px`, `h_px`).
  Join to `fsm_frame_features` for per-frame timestamps (`pts_ms`, `frame_utc_ms`).

Default is `false` to keep the database minimal.

## Optional TCN feature storage

When `db_config.json` sets `EnableTcnFeatures` to `true`, the writer saves `tcn_features`
and registers `tcn_feature_versions`. Default is `false` to avoid extra storage.

## Optional TCN inference

When `db_config.json` sets `EnableTcnInference` to `true`, the app initializes the
TCN inference engine and consumes features for prediction. Default is `false`.

## Online analysis (no TCN)

Lightweight online analysis is configured via `analysis_config.json`.
See `docs/ANALYSIS_FRAMEWORK.md` for details.

## Retention strategy

Use run-level deletion only (never delete isolated frames first):

1. Keep recent runs by age (`cutoff_utc_ms`).
2. Keep newest `max_runs`.
3. Keep within `max_frames` total.
4. After cleanup run checkpoint and optimize.

See `sql/retention_v4.sql`.

## Integration order (recommended)

1. Introduce run lifecycle in C# pipeline (`start run`, `end run`).
2. Write FSM frame features with `run_id`.
3. Update label writer and Python tool to select/write by `run_id`.

## Distance view for FSM

`v_fsm_dist` is now a direct projection of `fsm_frame_features` (no per-detection storage).

- Center object: fixed `class_id = 2` (highest score instance per frame/model).
- Targets: `class_id = 0` and `class_id = 1`.
- Per frame/target class, only the nearest target instance to class 2 is retained at write time.
- Output granularity: one row per `run_id + model_id + frame_index`.
- Outputs include:
  - `dist_id0_to_id2_q1000`, `dist_id1_to_id2_q1000`
  - `center_score_q1000`, `score_id0_q1000`, `score_id1_q1000`
  - `area_id0_px`, `area_id1_px`, `area_id2_px`
  - Missing is encoded as `0xFFFF` in `dist_id*_to_id2_q1000`
