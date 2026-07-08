from __future__ import annotations

import sqlite3
import time
from pathlib import Path
from typing import Any

import pandas as pd
import plotly.graph_objects as go
import streamlit as st


APP_DIR = Path(__file__).resolve().parent
VIDEO_DEMO_ROOT = APP_DIR.parent


def choose_existing_path(*candidates: Path) -> Path:
    for candidate in candidates:
        if candidate.exists():
            return candidate
    return candidates[0]


DEFAULT_DB_PATH = choose_existing_path(
    VIDEO_DEMO_ROOT / "bin" / "Release" / "net8.0-windows" / "inference.db",
    VIDEO_DEMO_ROOT / "bin" / "Debug" / "net8.0-windows" / "inference.db",
    VIDEO_DEMO_ROOT / "bin" / "Release" / "net8.0-windows7.0" / "inference.db",
    VIDEO_DEMO_ROOT / "bin" / "Debug" / "net8.0-windows7.0" / "inference.db",
    VIDEO_DEMO_ROOT / "inference.db",
)

METRIC_LABEL_TO_COL = {
    "Detection Count": "det_count",
    "Normalized Area Sum": "area_norm_sum",
    "Average Score": "score_avg",
    "Center X (Normalized)": "cx_norm_avg",
    "Center Y (Normalized)": "cy_norm_avg",
}


def resolve_path(raw: str, base: Path) -> Path:
    path = Path(raw.strip())
    if path.is_absolute():
        return path
    return (base / path).resolve()


def open_connection(db_path: Path) -> sqlite3.Connection:
    conn = sqlite3.connect(str(db_path))
    conn.row_factory = sqlite3.Row
    return conn


def fetch_dataframe(conn: sqlite3.Connection, sql: str, params: tuple[Any, ...] = ()) -> pd.DataFrame:
    return pd.read_sql_query(sql, conn, params=params)


def list_missing_views(conn: sqlite3.Connection) -> list[str]:
    required = {"v_run_summary"}
    rows = conn.execute("SELECT name FROM sqlite_master WHERE type='view';").fetchall()
    available = {str(r["name"]) for r in rows}
    return sorted(required - available)


def load_runs(conn: sqlite3.Connection) -> pd.DataFrame:
    if has_view(conn, "v_run_summary"):
        df = fetch_dataframe(
            conn,
            """
SELECT *
FROM v_run_summary
ORDER BY started_utc_ms DESC;
""",
        )

        defaults: dict[str, Any] = {
            "run_id": 0,
            "run_uuid": "",
            "source_key": "",
            "status": "unknown",
            "started_utc_ms": 0,
            "ended_utc_ms": None,
            "frame_count": 0,
            "model_count": 0,
            "avg_det_per_frame": None,
        }
        for col, default_value in defaults.items():
            if col not in df.columns:
                df[col] = default_value

        return df[
            [
                "run_id",
                "run_uuid",
                "source_key",
                "status",
                "started_utc_ms",
                "ended_utc_ms",
                "frame_count",
                "model_count",
                "avg_det_per_frame",
            ]
        ]

    return fetch_dataframe(
        conn,
        """
WITH frame_counts AS (
  SELECT run_id, COUNT(*) AS frame_count
  FROM frames
  GROUP BY run_id
)
SELECT
  r.id AS run_id,
  r.run_uuid,
  s.source_key,
  r.status,
  r.started_utc_ms,
  r.ended_utc_ms,
  COALESCE(fc.frame_count, 0) AS frame_count,
  0 AS model_count,
  NULL AS avg_det_per_frame
FROM inference_runs r
JOIN sources s ON s.id = r.source_id
LEFT JOIN frame_counts fc ON fc.run_id = r.id
ORDER BY r.started_utc_ms DESC;
""",
        )


def load_classes(conn: sqlite3.Connection, run_id: int) -> pd.DataFrame:
    return fetch_dataframe(
        conn,
        """
SELECT DISTINCT class_id, class_name
FROM v_curve_dense
WHERE run_id = ?
ORDER BY class_id;
""",
        (run_id,),
    )


def load_curve_dense(conn: sqlite3.Connection, run_id: int) -> pd.DataFrame:
    return fetch_dataframe(
        conn,
        """
SELECT
  run_id,
  frame_index,
  pts_ms,
  frame_utc_ms,
  class_id,
  class_name,
  det_count,
  score_avg,
  area_norm_sum,
  cx_norm_avg,
  cy_norm_avg
FROM v_curve_dense
WHERE run_id = ?
ORDER BY pts_ms ASC, class_id ASC;
""",
        (run_id,),
    )


def load_ts_map(conn: sqlite3.Connection, run_id: int, limit: int = 2000) -> pd.DataFrame:
    return fetch_dataframe(
        conn,
        """
SELECT
  run_id,
  frame_index,
  pts_ms,
  frame_utc_ms,
  total_det_count,
  total_area_norm_sum,
  class_count_map,
  class_area_map
FROM v_curve_ts_map
WHERE run_id = ?
ORDER BY pts_ms ASC
LIMIT ?;
""",
        (run_id, limit),
    )


def load_distance(conn: sqlite3.Connection, run_id: int) -> pd.DataFrame:
    return fetch_dataframe(
        conn,
        """
SELECT
  run_id,
  model_id,
  frame_index,
  pts_ms,
  frame_utc_ms,
  center_class_id,
  center_score_q1000,
  score_id0_q1000,
  score_id1_q1000,
  dist_id0_to_id2_q1000,
  dist_id1_to_id2_q1000,
  area_id0_px,
  area_id1_px,
  area_id2_px
FROM v_fsm_dist
WHERE run_id = ?
ORDER BY pts_ms ASC;
""",
        (run_id,),
    )


def load_labels(conn: sqlite3.Connection, run_id: int) -> pd.DataFrame:
    return fetch_dataframe(
        conn,
        """
SELECT
  id,
  run_id,
  step_index,
  label,
  source_type,
  score_q / 10000.0 AS score,
  start_pts_ms,
  end_pts_ms,
  start_utc_ms,
  end_utc_ms,
  created_utc_ms,
  updated_utc_ms
FROM fsm_labels
WHERE run_id = ?
ORDER BY start_pts_ms ASC, id ASC;
""",
        (run_id,),
    )


def has_view(conn: sqlite3.Connection, view_name: str) -> bool:
    row = conn.execute(
        "SELECT 1 FROM sqlite_master WHERE type='view' AND name=? LIMIT 1;",
        (view_name,),
    ).fetchone()
    return row is not None


def has_table(conn: sqlite3.Connection, table_name: str) -> bool:
    row = conn.execute(
        "SELECT 1 FROM sqlite_master WHERE type='table' AND name=? LIMIT 1;",
        (table_name,),
    ).fetchone()
    return row is not None


def score_to_q(score: float | None) -> int | None:
    if score is None:
        return None
    value = max(0.0, min(1.0, score))
    return int(round(value * 10000))


def upsert_label(
    conn: sqlite3.Connection,
    run_id: int,
    run_started_utc_ms: int,
    step_index: int,
    label: str,
    source_type: str,
    score: float | None,
    start_pts_ms: int,
    end_pts_ms: int,
) -> None:
    now_ms = int(time.time() * 1000)
    start_utc_ms = run_started_utc_ms + max(0, start_pts_ms)
    end_utc_ms = run_started_utc_ms + max(0, end_pts_ms)
    if end_utc_ms <= start_utc_ms:
        end_utc_ms = start_utc_ms + 1

    score_q = score_to_q(score)
    with conn:
        conn.execute(
            """
INSERT INTO fsm_labels (
  run_id,
  step_index,
  label,
  source_type,
  score_q,
  start_pts_ms,
  end_pts_ms,
  start_utc_ms,
  end_utc_ms,
  created_utc_ms,
  updated_utc_ms)
VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
ON CONFLICT(run_id, source_type, step_index, label, start_utc_ms, end_utc_ms) DO UPDATE SET
  score_q = COALESCE(excluded.score_q, fsm_labels.score_q),
  start_pts_ms = excluded.start_pts_ms,
  end_pts_ms = excluded.end_pts_ms,
  updated_utc_ms = excluded.updated_utc_ms;
""",
            (
                run_id,
                step_index,
                label,
                source_type,
                score_q,
                start_pts_ms,
                end_pts_ms,
                start_utc_ms,
                end_utc_ms,
                now_ms,
                now_ms,
            ),
        )


def delete_label(conn: sqlite3.Connection, label_id: int, run_id: int) -> None:
    with conn:
        conn.execute("DELETE FROM fsm_labels WHERE id = ? AND run_id = ?;", (label_id, run_id))


def build_figure(curve_df: pd.DataFrame, metric_col: str, selected_classes: list[int]) -> go.Figure:
    fig = go.Figure()
    if curve_df.empty:
        return fig

    for class_id in selected_classes:
        class_df = curve_df[curve_df["class_id"] == class_id].sort_values("pts_ms")
        if class_df.empty:
            continue
        class_name = str(class_df["class_name"].iloc[0])
        fig.add_trace(
            go.Scatter(
                x=class_df["pts_ms"] / 1000.0,
                y=class_df[metric_col],
                mode="lines+markers",
                name=f"{class_id}: {class_name}",
                customdata=class_df[["frame_index", "pts_ms", "det_count", "score_avg"]].values,
                hovertemplate=(
                    "t=%{x:.3f}s<br>value=%{y:.6f}<br>"
                    "frame=%{customdata[0]}<br>pts=%{customdata[1]} ms<br>"
                    "det=%{customdata[2]}<br>score=%{customdata[3]:.4f}<extra></extra>"
                ),
            )
        )

    fig.update_layout(
        height=520,
        margin=dict(l=20, r=20, t=30, b=20),
        legend=dict(orientation="h", yanchor="bottom", y=1.02, xanchor="left", x=0),
    )
    fig.update_xaxes(title_text="PTS (seconds)")
    fig.update_yaxes(title_text=metric_col)
    return fig


def build_distance_figure(dist_df: pd.DataFrame, model_id: int) -> go.Figure:
    fig = go.Figure()
    if dist_df.empty:
        return fig

    model_df = dist_df[(dist_df["model_id"].astype(int) == int(model_id))].copy()
    if model_df.empty:
        return fig

    model_df = model_df.sort_values("pts_ms")
    model_df["dist0_raw"] = pd.to_numeric(model_df["dist_id0_to_id2_q1000"], errors="coerce")
    model_df["dist1_raw"] = pd.to_numeric(model_df["dist_id1_to_id2_q1000"], errors="coerce")
    model_df["id0_missing"] = model_df["dist0_raw"] >= 65535
    model_df["id1_missing"] = model_df["dist1_raw"] >= 65535
    model_df["dist0_q1000"] = model_df["dist0_raw"].where(~model_df["id0_missing"], 1000).clip(lower=0, upper=1000)
    model_df["dist1_q1000"] = model_df["dist1_raw"].where(~model_df["id1_missing"], 1000).clip(lower=0, upper=1000)

    fig.add_trace(
        go.Scatter(
            x=model_df["pts_ms"] / 1000.0,
            y=model_df["dist0_q1000"],
            mode="lines",
            name="0 -> 2",
            line=dict(color="#1f77b4", width=2),
            customdata=model_df[
                ["frame_index", "pts_ms", "id0_missing", "area_id0_px", "score_id0_q1000"]
            ].values,
            hovertemplate=(
                "t=%{x:.3f}s<br>dist_q1000=%{y:.0f}<br>"
                "frame=%{customdata[0]}<br>pts=%{customdata[1]} ms<br>"
                "missing=%{customdata[2]}<br>area=%{customdata[3]}<br>"
                "score_q1000=%{customdata[4]}<extra></extra>"
            ),
        )
    )
    fig.add_trace(
        go.Scatter(
            x=model_df["pts_ms"] / 1000.0,
            y=model_df["dist1_q1000"],
            mode="lines",
            name="1 -> 2",
            line=dict(color="#2ca02c", width=2),
            customdata=model_df[
                ["frame_index", "pts_ms", "id1_missing", "area_id1_px", "score_id1_q1000"]
            ].values,
            hovertemplate=(
                "t=%{x:.3f}s<br>dist_q1000=%{y:.0f}<br>"
                "frame=%{customdata[0]}<br>pts=%{customdata[1]} ms<br>"
                "missing=%{customdata[2]}<br>area=%{customdata[3]}<br>"
                "score_q1000=%{customdata[4]}<extra></extra>"
            ),
        )
    )

    miss0 = model_df[model_df["id0_missing"].astype(int) == 1]
    if not miss0.empty:
        fig.add_trace(
            go.Scatter(
                x=miss0["pts_ms"] / 1000.0,
                y=miss0["dist0_q1000"],
                mode="markers",
                name="0 -> 2 missing",
                marker=dict(color="#1f77b4", symbol="x", size=8),
                hoverinfo="skip",
            )
        )

    miss1 = model_df[model_df["id1_missing"].astype(int) == 1]
    if not miss1.empty:
        fig.add_trace(
            go.Scatter(
                x=miss1["pts_ms"] / 1000.0,
                y=miss1["dist1_q1000"],
                mode="markers",
                name="1 -> 2 missing",
                marker=dict(color="#2ca02c", symbol="x", size=8),
                hoverinfo="skip",
            )
        )

    fig.update_layout(
        height=420,
        margin=dict(l=20, r=20, t=30, b=20),
        legend=dict(orientation="h", yanchor="bottom", y=1.02, xanchor="left", x=0),
    )
    fig.update_xaxes(title_text="PTS (seconds)")
    fig.update_yaxes(title_text="Distance q1000 to class 2 (0..1000, 0xFFFF=missing)", range=[0, 1020])
    return fig


def main() -> None:
    st.set_page_config(page_title="Curve Label Tool (Run-based)", layout="wide")
    st.title("Curve Label Tool (Run-based)")

    db_raw = st.sidebar.text_input("SQLite DB path", str(DEFAULT_DB_PATH))
    db_path = resolve_path(db_raw, APP_DIR)
    st.sidebar.caption(f"Resolved path: {db_path}")

    if not db_path.exists():
        st.error(f"Database file not found: {db_path}")
        st.stop()

    conn: sqlite3.Connection | None = None
    try:
        conn = open_connection(db_path)

        if not has_table(conn, "inference_runs") or not has_table(conn, "fsm_labels"):
            st.error("This database is not v4-ready. Missing table: inference_runs or fsm_labels.")
            st.stop()

        missing_views = list_missing_views(conn)
        if missing_views:
            st.warning("Missing views: " + ", ".join(missing_views) + ".")

        has_distance_view = has_view(conn, "v_fsm_dist")
        if not has_distance_view:
            st.warning("Missing view: v_fsm_dist. Distance panel will be disabled.")

        runs_df = load_runs(conn)
        if runs_df.empty:
            st.info("No inference runs found.")
            st.stop()

        run_options = [
            f"run_id={int(r.run_id)} | {r.status} | {r.source_key} | start={int(r.started_utc_ms)} | frames={int(r.frame_count)}"
            for r in runs_df.itertuples(index=False)
        ]
        selected_run_idx = st.selectbox(
            "Select Run",
            options=list(range(len(run_options))),
            format_func=lambda i: run_options[i],
            index=0,
        )
        selected_run = runs_df.iloc[int(selected_run_idx)]
        run_id = int(selected_run["run_id"])
        run_uuid = str(selected_run["run_uuid"])
        run_started_utc_ms = int(selected_run["started_utc_ms"])

        st.caption(
            f"Run UUID: {run_uuid} | Source: {selected_run['source_key']} | "
            f"Status: {selected_run['status']}"
        )

        curve_df = pd.DataFrame()
        classes_df = pd.DataFrame()
        has_curve_views = has_view(conn, "v_curve_dense") and has_view(conn, "v_curve_ts_map")
        if has_curve_views:
            curve_df = load_curve_dense(conn, run_id)
            classes_df = load_classes(conn, run_id)

        left_col, right_col = st.columns([3, 2])
        with left_col:
            st.subheader("Curve")
            if not has_curve_views:
                st.caption("Curve views are not available in the minimal schema.")
            else:
                metric_label = st.selectbox("Metric", list(METRIC_LABEL_TO_COL.keys()), index=0)
                metric_col = METRIC_LABEL_TO_COL[metric_label]

                if curve_df.empty:
                    st.info("No curve rows for this run.")
                else:
                    class_options = classes_df["class_id"].astype(int).tolist()
                    default_classes = class_options[: min(5, len(class_options))]
                    selected_classes = st.multiselect(
                        "Classes",
                        options=class_options,
                        default=default_classes,
                        format_func=lambda cid: f"{cid}: {classes_df.loc[classes_df['class_id'] == cid, 'class_name'].iloc[0]}",
                    )
                    if not selected_classes:
                        st.info("Select at least one class.")
                    else:
                        fig = build_figure(curve_df, metric_col, selected_classes)
                        st.plotly_chart(fig, use_container_width=True)

        with right_col:
            st.subheader("Run Snapshot")
            if has_curve_views:
                ts_map_df = load_ts_map(conn, run_id)
                st.dataframe(ts_map_df, use_container_width=True, hide_index=True)
            else:
                st.caption("Curve views missing. Snapshot disabled.")

        st.divider()
        st.subheader("Distance to Class 2 (One Row Per Frame)")
        if not has_distance_view:
            st.info("View v_fsm_dist is unavailable in this database.")
        else:
            dist_df = load_distance(conn, run_id)
            if dist_df.empty:
                st.info("No distance rows for this run.")
            else:
                model_options = sorted(dist_df["model_id"].dropna().astype(int).unique().tolist())
                if not model_options:
                    st.info("No DET model rows found for distance plotting.")
                else:
                    selected_dist_model = st.selectbox(
                        "Distance Model",
                        options=model_options,
                        format_func=lambda m: f"model_id={m}",
                        key=f"distance_model_{run_id}",
                    )
                    dist_fig = build_distance_figure(dist_df, int(selected_dist_model))
                    st.plotly_chart(dist_fig, use_container_width=True)
                    st.caption(
                        "Center class is fixed to 2. Curves show 0 -> 2 and 1 -> 2 in q1000 scale (0..1000). "
                        "Missing points are marked with X."
                    )

        st.divider()
        st.subheader("Label Editor (fsm_labels)")

        labels_df = load_labels(conn, run_id)
        st.dataframe(labels_df, use_container_width=True, hide_index=True)

        with st.form("label_form", clear_on_submit=False):
            step_index = st.number_input("Step Index (-1 for none)", value=-1, step=1)
            label = st.text_input("Label", value="")
            source_type = st.selectbox("Source Type", ["manual", "manual_click", "tcn", "default"], index=0)
            start_pts_ms = st.number_input("Start PTS (ms)", min_value=0, value=0, step=1)
            end_pts_ms = st.number_input("End PTS (ms)", min_value=0, value=1000, step=1)
            score_raw = st.text_input("Score (optional, 0..1)", value="")
            submitted = st.form_submit_button("Upsert Label")

        if submitted:
            clean_label = label.strip()
            if not clean_label:
                st.error("Label cannot be empty.")
            elif int(end_pts_ms) <= int(start_pts_ms):
                st.error("End PTS must be greater than Start PTS.")
            else:
                score: float | None = None
                if score_raw.strip():
                    try:
                        score = float(score_raw)
                    except ValueError:
                        st.error("Score is not a valid float.")
                        st.stop()

                upsert_label(
                    conn=conn,
                    run_id=run_id,
                    run_started_utc_ms=run_started_utc_ms,
                    step_index=int(step_index),
                    label=clean_label,
                    source_type=source_type.strip().lower(),
                    score=score,
                    start_pts_ms=int(start_pts_ms),
                    end_pts_ms=int(end_pts_ms),
                )
                st.success("Label upserted.")
                st.rerun()

        st.subheader("Delete Label")
        delete_id = st.number_input("Label ID", min_value=0, value=0, step=1)
        if st.button("Delete by ID"):
            delete_label(conn, int(delete_id), run_id)
            st.success(f"Deleted label id={int(delete_id)} for run_id={run_id}.")
            st.rerun()

    finally:
        if conn is not None:
            conn.close()


if __name__ == "__main__":
    main()

