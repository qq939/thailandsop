-- Retention policy templates for schema_v4.sql
--
-- All deletes are run-level deletes. Child rows are removed by ON DELETE CASCADE.
-- Recommended execution order:
--   1) age-based retention
--   2) max-runs retention
--   3) max-frames retention (optional)
--   4) maintenance (checkpoint/optimize/vacuum)

PRAGMA foreign_keys = ON;

-- ---------------------------------------------------------------------------
-- 1) Age-based retention
-- Keep runs with started_utc_ms >= :cutoff_utc_ms
-- Example cutoff: now_utc_ms - 30*24*3600*1000
-- ---------------------------------------------------------------------------

DELETE FROM inference_runs
WHERE id IN (
  SELECT id
  FROM inference_runs
  WHERE started_utc_ms < :cutoff_utc_ms
  ORDER BY started_utc_ms ASC, id ASC
  LIMIT :delete_batch
);

-- ---------------------------------------------------------------------------
-- 2) Count-based retention (keep newest :max_runs runs)
-- ---------------------------------------------------------------------------

WITH ranked AS (
  SELECT
    id,
    ROW_NUMBER() OVER (ORDER BY started_utc_ms DESC, id DESC) AS rn
  FROM inference_runs
)
DELETE FROM inference_runs
WHERE id IN (
  SELECT id
  FROM ranked
  WHERE rn > :max_runs
  ORDER BY rn DESC
  LIMIT :delete_batch
);

-- ---------------------------------------------------------------------------
-- 3) Capacity-based retention (keep newest runs within :max_frames total)
-- ---------------------------------------------------------------------------

WITH frame_counts AS (
  SELECT run_id, COUNT(*) AS frame_count
  FROM fsm_frame_features
  GROUP BY run_id
),
ordered_runs AS (
  SELECT
    r.id,
    r.started_utc_ms,
    COALESCE(fc.frame_count, 0) AS frame_count,
    SUM(COALESCE(fc.frame_count, 0)) OVER (
      ORDER BY r.started_utc_ms DESC, r.id DESC
      ROWS UNBOUNDED PRECEDING
    ) AS cumulative_frames
  FROM inference_runs r
  LEFT JOIN frame_counts fc ON fc.run_id = r.id
),
to_delete AS (
  SELECT id
  FROM ordered_runs
  WHERE cumulative_frames > :max_frames
  ORDER BY started_utc_ms ASC, id ASC
  LIMIT :delete_batch
)
DELETE FROM inference_runs
WHERE id IN (SELECT id FROM to_delete);

-- ---------------------------------------------------------------------------
-- 4) Post-cleanup maintenance
-- ---------------------------------------------------------------------------

PRAGMA wal_checkpoint(TRUNCATE);
PRAGMA optimize;

-- If long-running service cannot afford full VACUUM, use incremental vacuum strategy:
--   PRAGMA auto_vacuum = INCREMENTAL;   -- set once when DB is created
--   PRAGMA incremental_vacuum(2000);
--
-- For offline maintenance windows, run a full VACUUM:
--   VACUUM;
