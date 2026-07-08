#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT_PATH="${ROOT_DIR}/src/VideoInference.Jetson/VideoInference.Jetson.csproj"
OUTPUT_DIR="${ROOT_DIR}/src/VideoInference.Jetson/bin/Debug/net8.0/linux-arm64"
APP_PATH="${OUTPUT_DIR}/VideoInference.Jetson.dll"
PREPARE_OPENCV_SCRIPT="${ROOT_DIR}/scripts/jetson/prepare-opencv-runtime.sh"
PREPARE_FFMPEG_SCRIPT="${ROOT_DIR}/scripts/jetson/prepare-ffmpeg-runtime.sh"
PROJECT_FFMPEG_ROOT="${ROOT_DIR}/ThirdParty/FFmpeg/linux-arm64"

JETSON_YOLO_ORT_DEMO_ROOT="${JETSON_YOLO_ORT_DEMO_ROOT:-${HOME}/JetsonYoloOrtDemo}"
ORT_NATIVE_DIR="${ORT_NATIVE_DIR:-${JETSON_YOLO_ORT_DEMO_ROOT}/native}"
GPU_ROOTFS_DIR="${GPU_ROOTFS_DIR:-${JETSON_YOLO_ORT_DEMO_ROOT}/.local-gpu/rootfs}"
FFMPEG_ROOT="${FFMPEG_ROOT:-${PROJECT_FFMPEG_ROOT}}"
HIK_SDK_ROOT="${HIK_SDK_ROOT:-${ROOT_DIR}/ThirdParty/CameraHIK/linux-arm64}"

if [[ ! -d "${ORT_NATIVE_DIR}" ]]; then
  echo "Missing ORT native directory: ${ORT_NATIVE_DIR}" >&2
  echo "Set ORT_NATIVE_DIR or JETSON_YOLO_ORT_DEMO_ROOT to your validated Jetson ORT runtime." >&2
  exit 1
fi

mkdir -p "${OUTPUT_DIR}"

dotnet build "${PROJECT_PATH}" -c Debug >/dev/null

if [[ -x "${PREPARE_OPENCV_SCRIPT}" ]]; then
  "${PREPARE_OPENCV_SCRIPT}" "${OUTPUT_DIR}" >/dev/null
fi

if [[ -x "${PREPARE_FFMPEG_SCRIPT}" ]]; then
  if [[ ! -f "${OUTPUT_DIR}/libavformat.so.60" || ! -f "${OUTPUT_DIR}/libavcodec.so.60" ]]; then
    if [[ -d "${FFMPEG_ROOT}" && -f "${FFMPEG_ROOT}/libavformat.so.60" && -f "${FFMPEG_ROOT}/libavcodec.so.60" ]]; then
      cp -a "${FFMPEG_ROOT}/libavcodec.so"* "${OUTPUT_DIR}/"
      cp -a "${FFMPEG_ROOT}/libavformat.so"* "${OUTPUT_DIR}/"
      cp -a "${FFMPEG_ROOT}/libavutil.so"* "${OUTPUT_DIR}/"
      cp -a "${FFMPEG_ROOT}/libswresample.so"* "${OUTPUT_DIR}/"
      cp -a "${FFMPEG_ROOT}/libswscale.so"* "${OUTPUT_DIR}/"
    else
      "${PREPARE_FFMPEG_SCRIPT}" "${OUTPUT_DIR}" >/dev/null
    fi
  fi
fi

cp "${ORT_NATIVE_DIR}/libonnxruntime.so.1.22.1" "${OUTPUT_DIR}/libonnxruntime.so"
cp "${ORT_NATIVE_DIR}/libonnxruntime_providers_shared.so" "${OUTPUT_DIR}/libonnxruntime_providers_shared.so"

if [[ -f "${ORT_NATIVE_DIR}/libonnxruntime_providers_cuda.so" ]]; then
  cp "${ORT_NATIVE_DIR}/libonnxruntime_providers_cuda.so" "${OUTPUT_DIR}/libonnxruntime_providers_cuda.so"
fi

if [[ -f "${ORT_NATIVE_DIR}/libonnxruntime_providers_tensorrt.so" ]]; then
  cp "${ORT_NATIVE_DIR}/libonnxruntime_providers_tensorrt.so" "${OUTPUT_DIR}/libonnxruntime_providers_tensorrt.so"
fi

LD_PATHS=(
  "${OUTPUT_DIR}"
)

if [[ -d "${GPU_ROOTFS_DIR}" ]]; then
  LD_PATHS+=(
    "${GPU_ROOTFS_DIR}/usr/local/cuda/lib64"
    "${GPU_ROOTFS_DIR}/usr/local/cuda/targets/aarch64-linux/lib"
    "${GPU_ROOTFS_DIR}/usr/lib/aarch64-linux-gnu"
    "${GPU_ROOTFS_DIR}/usr/lib/aarch64-linux-gnu/nvidia"
  )
fi

if [[ -d "/usr/lib/aarch64-linux-gnu/nvidia" ]]; then
  LD_PATHS+=("/usr/lib/aarch64-linux-gnu/nvidia")
fi

if [[ -n "${FFMPEG_ROOT}" && -d "${FFMPEG_ROOT}" ]]; then
  LD_PATHS+=("${FFMPEG_ROOT}")
fi

if [[ -d "${HIK_SDK_ROOT}" ]]; then
  LD_PATHS+=("${HIK_SDK_ROOT}")
fi

if [[ -n "${LD_LIBRARY_PATH:-}" ]]; then
  LD_PATHS+=("${LD_LIBRARY_PATH}")
fi

export LD_LIBRARY_PATH
LD_LIBRARY_PATH="$(IFS=:; echo "${LD_PATHS[*]}")"

echo "Running VideoInference.Jetson from ${APP_PATH}"
echo "ORT runtime : ${ORT_NATIVE_DIR}"
if [[ -d "${GPU_ROOTFS_DIR}" ]]; then
  echo "GPU rootfs   : ${GPU_ROOTFS_DIR}"
else
  echo "GPU rootfs   : not configured"
fi
if [[ -n "${FFMPEG_ROOT}" ]]; then
  echo "FFmpeg root  : ${FFMPEG_ROOT}"
fi
if [[ -d "${HIK_SDK_ROOT}" ]]; then
  echo "Hik SDK root : ${HIK_SDK_ROOT}"
fi

exec dotnet "${APP_PATH}" "$@"
