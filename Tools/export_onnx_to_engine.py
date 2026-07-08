#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Export ONNX to TensorRT engine using trtexec.

Example:
  python Tools/export_onnx_to_engine.py --onnx DL/best.onnx --engine DL/best.engine --fp16
"""

from __future__ import annotations

import argparse
from pathlib import Path
import shutil
import subprocess
import sys


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Export ONNX to TensorRT engine via trtexec")
    parser.add_argument("--onnx", required=True, help="Path to the input ONNX model")
    parser.add_argument("--engine", required=True, help="Output TensorRT engine path")
    parser.add_argument("--trtexec", default="", help="Path to trtexec executable (optional if in PATH)")
    parser.add_argument("--fp16", action="store_true", help="Enable FP16 precision")
    parser.add_argument("--int8", action="store_true", help="Enable INT8 precision (requires calibration)")
    parser.add_argument("--workspace", type=int, default=4096, help="Workspace size in MB (default: 4096)")
    parser.add_argument("--min", default="", help="Min shapes, e.g. input:1x3x640x640")
    parser.add_argument("--opt", default="", help="Opt shapes, e.g. input:1x3x640x640")
    parser.add_argument("--max", default="", help="Max shapes, e.g. input:1x3x1280x1280")
    parser.add_argument("--verbose", action="store_true", help="Verbose trtexec output")
    parser.add_argument("--force-fp32", action="store_true", help="Force FP32 fallback for old GPUs")
    return parser.parse_args()


def find_trtexec(path: str) -> Path:
    """Locate trtexec executable"""
    if path:
        exe = Path(path).expanduser().resolve()
        if exe.exists():
            return exe

    exe = shutil.which("trtexec")
    if exe:
        return Path(exe).resolve()

    # Default installation path for Windows
    default = Path("C:/TensorRT-10.14.1.48/bin/trtexec.exe")
    return default


def detect_gpu_arch() -> str:
    """
    Detect GPU compute capability (SM) using nvidia-smi and map to CUDA architecture.
    Returns string like 'sm_61'.
    """
    try:
        import torch
        if torch.cuda.is_available():
            major, minor = torch.cuda.get_device_capability()
            return f"sm_{major}{minor}"
    except ImportError:
        pass
    # Fallback: default Pascal
    return "sm_61"


def build_trtexec_cmd(args: argparse.Namespace) -> list[str]:
    cmd = [
        str(find_trtexec(args.trtexec)),
        f"--onnx={Path(args.onnx).expanduser().resolve()}",
        f"--saveEngine={Path(args.engine).expanduser().resolve()}",
        f"--memPoolSize=workspace:{args.workspace}",
        f"--gpu-architecture={detect_gpu_arch()}",
    ]

    if args.fp16:
        cmd.append("--fp16")
    if args.int8:
        cmd.append("--int8")
    if args.force_fp32:
        cmd.append("--forceFallback")
    if args.verbose:
        cmd.append("--verbose")

    if args.min and args.opt and args.max:
        cmd.extend([
            f"--minShapes={args.min}",
            f"--optShapes={args.opt}",
            f"--maxShapes={args.max}",
        ])
    return cmd


def main() -> int:
    args = parse_args()
    onnx_path = Path(args.onnx).expanduser().resolve()
    engine_path = Path(args.engine).expanduser().resolve()

    if not onnx_path.exists():
        print(f"[ERROR] ONNX file not found: {onnx_path}")
        return 2

    engine_path.parent.mkdir(parents=True, exist_ok=True)

    cmd = build_trtexec_cmd(args)
    print("[INFO] Running:", " ".join(cmd))

    # Run trtexec
    completed = subprocess.run(cmd, capture_output=False)
    if completed.returncode != 0:
        print(f"[ERROR] trtexec failed with code {completed.returncode}")
        return completed.returncode

    if engine_path.exists():
        print(f"[OK] Engine saved: {engine_path}")
        print("[NOTE] TensorRT engine is GPU-specific. Regenerate if GPU changes.")
        return 0

    print("[ERROR] Engine file was not created.")
    return 4


if __name__ == "__main__":
    sys.exit(main())
