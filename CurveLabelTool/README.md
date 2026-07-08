# CurveLabelTool

Streamlit tool for inspecting run-based detection curves and editing `fsm_labels` in the v4 schema.

## Features

- Select an inference run from `v_run_summary`.
- Plot fixed-center distance curves (`0 -> 2` and `1 -> 2`) from `v_fsm_dist` (one row per frame, q1000 scale).
- Optional: if `v_curve_*` views exist, the curve panel will show per-class metrics and snapshots.
- View and edit labels in `fsm_labels` (upsert by run + UTC segment key).

## Quick Start

1. Prepare environment:

```powershell
cd VideoInferenceDemo\CurveLabelTool
.\setup_env.ps1 -UseLock
```

2. Run app:

```powershell
.\run.ps1
```

3. In the UI:

- Set SQLite path (default prefers `bin/Release/net8.0-windows/inference.db`).
- Choose a run.
- Select metric/classes for plotting.
- Add or delete labels for the selected run.

## Notes

- This tool targets the minimal v4 layout (`inference_runs`, `fsm_labels`, `fsm_frame_features`, `v_fsm_dist`).
- If `v_curve_*` views are missing, the curve panel is disabled and label editing remains available.

