#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUTPUT_DIR="${1:-${ROOT_DIR}/src/VideoInference.Jetson/bin/Debug/net8.0/linux-arm64}"

OPENCVSHARP_TAG="${OPENCVSHARP_TAG:-4.11.0.20250507}"
CACHE_ROOT="${JETSON_OPENCV_CACHE_ROOT:-${ROOT_DIR}/.cache/jetson-opencv}"
SRC_DIR="${CACHE_ROOT}/opencvsharp-src"
BUILD_DIR="${CACHE_ROOT}/build"
LOCALROOT_DIR="${CACHE_ROOT}/opencv-localroot"
DEB_DIR="${CACHE_ROOT}/debs"
CMAKE_BIN="${CMAKE_BIN:-}"

if [[ -z "${CMAKE_BIN}" ]]; then
  if command -v cmake >/dev/null 2>&1; then
    CMAKE_BIN="$(command -v cmake)"
  elif [[ -x "${HOME}/JetsonYoloOrtDemo/.local-build-tools/bin/cmake" ]]; then
    CMAKE_BIN="${HOME}/JetsonYoloOrtDemo/.local-build-tools/bin/cmake"
  else
    echo "cmake was not found. Set CMAKE_BIN or bootstrap local build tools first." >&2
    exit 1
  fi
fi

mkdir -p "${OUTPUT_DIR}" "${CACHE_ROOT}" "${DEB_DIR}"

if [[ -f "${OUTPUT_DIR}/libOpenCvSharpExtern.so" && -f "${OUTPUT_DIR}/libopencv_videoio.so.4.5d" ]]; then
  echo "OpenCvSharp runtime already present in ${OUTPUT_DIR}"
  exit 0
fi

if [[ ! -d "${SRC_DIR}/.git" ]]; then
  rm -rf "${SRC_DIR}"
  git clone --depth 1 --branch "${OPENCVSHARP_TAG}" https://github.com/shimat/opencvsharp.git "${SRC_DIR}"
fi

packages=(
  libopencv-dev
  libopencv-calib3d-dev
  libopencv-contrib-dev
  libopencv-core-dev
  libopencv-dnn-dev
  libopencv-features2d-dev
  libopencv-flann-dev
  libopencv-highgui-dev
  libopencv-imgcodecs-dev
  libopencv-imgproc-dev
  libopencv-ml-dev
  libopencv-objdetect-dev
  libopencv-photo-dev
  libopencv-shape-dev
  libopencv-stitching-dev
  libopencv-superres-dev
  libopencv-video-dev
  libopencv-videoio-dev
  libopencv-videostab-dev
  libopencv-viz-dev
  libopencv-videoio4.5d
  libopencv-superres4.5d
  libopencv-videostab4.5d
  libswscale5
)

mkdir -p "${LOCALROOT_DIR}"
pushd "${DEB_DIR}" >/dev/null
for pkg in "${packages[@]}"; do
  if ls "${pkg}"_*_arm64.deb >/dev/null 2>&1; then
    continue
  fi

  if [[ "${pkg}" == libswscale5 ]]; then
    apt download "${pkg}" >/dev/null
  else
    apt download "${pkg}=4.5.4+dfsg-9ubuntu4" >/dev/null
  fi
done

for deb in ./*.deb; do
  dpkg-deb -x "${deb}" "${LOCALROOT_DIR}"
done
popd >/dev/null

mkdir -p "${LOCALROOT_DIR}/usr/lib/aarch64-linux-gnu"
for lib in /usr/lib/aarch64-linux-gnu/libopencv_*.so*; do
  ln -sf "${lib}" "${LOCALROOT_DIR}/usr/lib/aarch64-linux-gnu/$(basename "${lib}")"
done

python3 - "${SRC_DIR}" <<'PY'
from pathlib import Path
import re
import sys

src_dir = Path(sys.argv[1])

include_path = src_dir / "src/OpenCvSharpExtern/include_opencv.h"
text = include_path.read_text()
text = text.replace('#include <opencv2/xfeatures2d.hpp>\n', '')
include_path.write_text(text)

cmake_path = src_dir / "src/OpenCvSharpExtern/CMakeLists.txt"
text = cmake_path.read_text()
text = re.sub(r'file\(GLOB OPENCVSHARP_FILES \*\.cpp\)', """set(OPENCVSHARP_FILES
    core.cpp
    dnn.cpp
    imgcodecs.cpp
    imgproc.cpp
    std_string.cpp
    std_vector.cpp
    videoio.cpp)""", text)
cmake_path.write_text(text)

core_path = src_dir / "src/OpenCvSharpExtern/core.h"
text = core_path.read_text()
text = text.replace(
    "    cv::reduceArgMax(*src, *dst, axis, lastIndex);\n",
    """#if (CV_VERSION_MAJOR > 4) || (CV_VERSION_MAJOR == 4 && CV_VERSION_MINOR >= 7)
    cv::reduceArgMax(*src, *dst, axis, lastIndex);
#else
    throw std::runtime_error("cv::reduceArgMax requires OpenCV 4.7+");
#endif
""")
text = text.replace(
    "    cv::reduceArgMin(*src, *dst, axis, lastIndex);\n",
    """#if (CV_VERSION_MAJOR > 4) || (CV_VERSION_MAJOR == 4 && CV_VERSION_MINOR >= 7)
    cv::reduceArgMin(*src, *dst, axis, lastIndex);
#else
    throw std::runtime_error("cv::reduceArgMin requires OpenCV 4.7+");
#endif
""")
core_path.write_text(text)
PY

rm -rf "${BUILD_DIR}"
"${CMAKE_BIN}" -S "${SRC_DIR}/src" -B "${BUILD_DIR}" -DCMAKE_BUILD_TYPE=Release -DOpenCV_DIR="${LOCALROOT_DIR}/usr/lib/aarch64-linux-gnu/cmake/opencv4"
"${CMAKE_BIN}" --build "${BUILD_DIR}" -j"$(nproc)"

cp "${BUILD_DIR}/OpenCvSharpExtern/libOpenCvSharpExtern.so" "${OUTPUT_DIR}/libOpenCvSharpExtern.so"
cp "${LOCALROOT_DIR}/usr/lib/aarch64-linux-gnu/libopencv_videoio.so"* "${OUTPUT_DIR}/"
cp "${LOCALROOT_DIR}/usr/lib/aarch64-linux-gnu/libopencv_superres.so"* "${OUTPUT_DIR}/"
cp "${LOCALROOT_DIR}/usr/lib/aarch64-linux-gnu/libopencv_videostab.so"* "${OUTPUT_DIR}/"
cp "${LOCALROOT_DIR}/usr/lib/aarch64-linux-gnu/libswscale.so"* "${OUTPUT_DIR}/"

echo "Prepared OpenCvSharp runtime in ${OUTPUT_DIR}"
