#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUTPUT_DIR="${1:-${ROOT_DIR}/src/VideoInference.Jetson/bin/Debug/net8.0/linux-arm64}"

FFMPEG_VERSION="${FFMPEG_VERSION:-6.1.1}"
CACHE_ROOT="${JETSON_FFMPEG_CACHE_ROOT:-${ROOT_DIR}/.cache/jetson-ffmpeg}"
SRC_DIR="${CACHE_ROOT}/ffmpeg-src"
BUILD_DIR="${CACHE_ROOT}/build"
PREFIX_DIR="${CACHE_ROOT}/prefix"
ARCHIVE_DIR="${CACHE_ROOT}/archives"

mkdir -p "${OUTPUT_DIR}" "${CACHE_ROOT}" "${ARCHIVE_DIR}"

ARCHIVE_NAME="ffmpeg-${FFMPEG_VERSION}.tar.xz"
ARCHIVE_PATH="${ARCHIVE_DIR}/${ARCHIVE_NAME}"
SOURCE_URL="https://ffmpeg.org/releases/${ARCHIVE_NAME}"

if [[ ! -f "${ARCHIVE_PATH}" ]]; then
  curl -L "${SOURCE_URL}" -o "${ARCHIVE_PATH}"
fi

rm -rf "${SRC_DIR}" "${BUILD_DIR}" "${PREFIX_DIR}"
mkdir -p "${SRC_DIR}" "${BUILD_DIR}" "${PREFIX_DIR}"
tar -xf "${ARCHIVE_PATH}" -C "${SRC_DIR}" --strip-components=1

pushd "${SRC_DIR}" >/dev/null
./configure \
  --prefix="${PREFIX_DIR}" \
  --disable-programs \
  --disable-doc \
  --disable-debug \
  --disable-static \
  --enable-shared \
  --enable-pic \
  --disable-avdevice \
  --disable-avfilter \
  --disable-postproc \
  --disable-network \
  --disable-indevs \
  --disable-outdevs \
  --disable-librist \
  --disable-libsrt \
  --disable-xlib \
  --disable-libxcb \
  --disable-libxcb-shm \
  --disable-libxcb-xfixes \
  --disable-zlib \
  --disable-bzlib
make -j"$(nproc)"
make install
popd >/dev/null

cp "${PREFIX_DIR}/lib/libavcodec.so."* "${OUTPUT_DIR}/"
cp "${PREFIX_DIR}/lib/libavformat.so."* "${OUTPUT_DIR}/"
cp "${PREFIX_DIR}/lib/libavutil.so."* "${OUTPUT_DIR}/"
cp "${PREFIX_DIR}/lib/libswresample.so."* "${OUTPUT_DIR}/"
cp "${PREFIX_DIR}/lib/libswscale.so."* "${OUTPUT_DIR}/"

for lib in libavcodec.so.60 libavformat.so.60 libavutil.so.58 libswresample.so.4 libswscale.so.7; do
  if [[ -f "${OUTPUT_DIR}/${lib}" ]]; then
    continue
  fi

  full="$(find "${OUTPUT_DIR}" -maxdepth 1 -name "${lib}.*" | sort | head -n 1)"
  if [[ -n "${full}" ]]; then
    ln -sf "$(basename "${full}")" "${OUTPUT_DIR}/${lib}"
  fi
done

echo "Prepared FFmpeg ${FFMPEG_VERSION} runtime in ${OUTPUT_DIR}"
