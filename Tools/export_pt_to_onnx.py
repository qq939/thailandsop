#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Export YOLO .pt model to ONNX using Ultralytics.

Example:
  conda activate yolo
  python Tools/export_pt_to_onnx.py --pt DL/best.pt --output DL/best.onnx --imgsz 640
"""

from __future__ import annotations

import argparse
from pathlib import Path
import shutil
import sys


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Export YOLO .pt to ONNX")
    parser.add_argument("--pt", required=True, help="Path to .pt file")
    parser.add_argument("--output", default="", help="Output .onnx path (optional)")
    parser.add_argument("--imgsz", type=int, default=640, help="Input image size (default: 640)")
    parser.add_argument("--batch", type=int, default=1, help="Batch size (default: 1)")
    parser.add_argument("--dynamic", action="store_true", help="Export dynamic input shape")
    parser.add_argument("--opset", type=int, default=17, help="ONNX opset version (default: 17)")
    parser.add_argument("--half", action="store_true", help="Export FP16 (if supported)")
    parser.add_argument("--simplify", action="store_true", help="Simplify ONNX (if supported)")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    pt_path = Path(args.pt).expanduser().resolve()
    if not pt_path.exists():
        print(f"[ERROR] .pt file not found: {pt_path}")
        return 2

    try:
        from ultralytics import YOLO
    except Exception as exc:  # pragma: no cover - environment specific
        print("[ERROR] Failed to import ultralytics. Make sure you are in conda env 'yolo' and it is installed.")
        print(f"Details: {exc}")
        return 3

    model = YOLO(str(pt_path))
    result = model.export(
        format="onnx",
        imgsz=args.imgsz,
        batch=args.batch,
        dynamic=args.dynamic,
        opset=args.opset,
        half=args.half,
        simplify=args.simplify,
    )

    # Ultralytics returns output path or a dict depending on version.
    onnx_path = None
    if isinstance(result, (str, Path)):
        onnx_path = Path(result)
    elif isinstance(result, dict):
        maybe = result.get("file")
        if maybe:
            onnx_path = Path(maybe)

    if onnx_path is None or not onnx_path.exists():
        print("[ERROR] Export did not produce an ONNX file.")
        return 4

    if args.output:
        out_path = Path(args.output).expanduser().resolve()
        out_path.parent.mkdir(parents=True, exist_ok=True)
        if onnx_path.resolve() != out_path:
            shutil.copy2(onnx_path, out_path)
        print(f"[OK] ONNX exported to: {out_path}")
    else:
        print(f"[OK] ONNX exported to: {onnx_path}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
