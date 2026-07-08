import argparse
import base64
import json
import sys
from pathlib import Path

import numpy as np

try:
    import mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision
except Exception as import_error:  # pragma: no cover
    mp = None
    python = None
    vision = None
    MEDIAPIPE_IMPORT_ERROR = import_error
else:
    MEDIAPIPE_IMPORT_ERROR = None


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--endpoint", required=True)
    parser.add_argument("--task-file", required=True)
    parser.add_argument("--worker-kind", required=True)
    return parser.parse_args()


def connect_pipe(endpoint: str):
    pipe_path = rf"\\.\pipe\{endpoint}"
    return open(pipe_path, "r+b", buffering=0)


def read_message(handle) -> dict | None:
    line = handle.readline()
    if not line:
        return None

    text = line.decode("utf-8").strip()
    if not text:
        return None

    return json.loads(text)


def write_message(handle, payload: dict) -> None:
    data = json.dumps(payload, separators=(",", ":")) + "\n"
    handle.write(data.encode("utf-8"))
    handle.flush()


def build_stub_hand_payload() -> dict:
    return {
        "hands": []
    }


def parse_float(config: dict, key: str, fallback: float) -> float:
    raw_value = config.get(key)
    if raw_value is None:
        return fallback

    try:
        return float(raw_value)
    except (TypeError, ValueError):
        return fallback


def parse_int(config: dict, key: str, fallback: int) -> int:
    raw_value = config.get(key)
    if raw_value is None:
        return fallback

    try:
        return int(raw_value)
    except (TypeError, ValueError):
        return fallback


def create_landmarker(task_file: Path, config: dict):
    if MEDIAPIPE_IMPORT_ERROR is not None or mp is None or python is None or vision is None:
        raise RuntimeError(f"MediaPipe import failed: {MEDIAPIPE_IMPORT_ERROR}")

    if not task_file.exists():
        raise FileNotFoundError(f"MediaPipe task file not found: {task_file}")

    base_options = python.BaseOptions(model_asset_path=str(task_file))
    options = vision.HandLandmarkerOptions(
        base_options=base_options,
        running_mode=vision.RunningMode.VIDEO,
        num_hands=parse_int(config, "maxHands", 2),
        min_hand_detection_confidence=parse_float(config, "minHandDetectionConfidence", 0.5),
        min_hand_presence_confidence=parse_float(config, "minHandPresenceConfidence", 0.5),
        min_tracking_confidence=parse_float(config, "minTrackingConfidence", 0.5),
    )
    return vision.HandLandmarker.create_from_options(options)


def decode_frame(frame: dict) -> np.ndarray:
    width = int(frame["width"])
    height = int(frame["height"])
    pixel_format = frame.get("pixelFormat", "BGR24")
    image_bytes = base64.b64decode(frame.get("imageBase64", ""))

    if pixel_format != "BGR24":
        raise ValueError(f"Unsupported pixel format: {pixel_format}")

    expected_length = width * height * 3
    if len(image_bytes) != expected_length:
        raise ValueError(
            f"Unexpected image byte length: {len(image_bytes)} != {expected_length}"
        )

    return np.frombuffer(image_bytes, dtype=np.uint8).reshape((height, width, 3))


def apply_roi(frame_bgr: np.ndarray, roi: dict | None) -> tuple[np.ndarray, tuple[int, int, int, int] | None]:
    if not roi:
        return frame_bgr, None

    x = max(0, int(roi.get("x", 0)))
    y = max(0, int(roi.get("y", 0)))
    width = max(1, int(roi.get("width", frame_bgr.shape[1])))
    height = max(1, int(roi.get("height", frame_bgr.shape[0])))
    x2 = min(frame_bgr.shape[1], x + width)
    y2 = min(frame_bgr.shape[0], y + height)
    if x >= x2 or y >= y2:
        return frame_bgr, None

    return frame_bgr[y:y2, x:x2], (x, y, x2 - x, y2 - y)


def bgr_to_mp_image(frame_bgr: np.ndarray):
    frame_rgb = frame_bgr[:, :, ::-1]
    return mp.Image(image_format=mp.ImageFormat.SRGB, data=frame_rgb)


def normalize_landmark(x: float, y: float, roi_offset: tuple[int, int, int, int] | None, full_width: int, full_height: int) -> tuple[float, float]:
    if roi_offset is None:
        return float(x), float(y)

    roi_x, roi_y, roi_width, roi_height = roi_offset
    abs_x = roi_x + (x * roi_width)
    abs_y = roi_y + (y * roi_height)
    return abs_x / max(1, full_width), abs_y / max(1, full_height)


def handedness_label(result, index: int) -> tuple[str, float]:
    if not result.handedness or index >= len(result.handedness):
        return "", 0.0

    candidates = result.handedness[index]
    if not candidates:
        return "", 0.0

    top = candidates[0]
    return getattr(top, "category_name", "") or "", float(getattr(top, "score", 0.0) or 0.0)


def build_hand_payload(result, roi_offset: tuple[int, int, int, int] | None, full_width: int, full_height: int) -> dict:
    hands: list[dict] = []
    landmarks_list = getattr(result, "hand_landmarks", None) or []

    for hand_index, landmarks in enumerate(landmarks_list):
        label, handedness_score = handedness_label(result, hand_index)
        points = []
        for point_index, landmark in enumerate(landmarks):
            normalized_x, normalized_y = normalize_landmark(
                float(getattr(landmark, "x", 0.0)),
                float(getattr(landmark, "y", 0.0)),
                roi_offset,
                full_width,
                full_height,
            )
            points.append(
                {
                    "index": point_index,
                    "x": normalized_x,
                    "y": normalized_y,
                    "z": float(getattr(landmark, "z", 0.0)),
                    "score": 1.0,
                }
            )

        hands.append(
            {
                "id": hand_index,
                "label": label,
                "score": handedness_score,
                "landmarks": points,
            }
        )

    return {"hands": hands}


def main() -> int:
    args = parse_args()
    task_path = Path(args.task_file)
    runtime_label = "MediaPipe / Python"
    landmarker = None

    try:
        with connect_pipe(args.endpoint) as pipe_handle:
            while True:
                message = read_message(pipe_handle)
                if message is None:
                    break

                message_type = message.get("type")
                if message_type == "hello":
                    config = message.get("config") or {}
                    landmarker = create_landmarker(task_path, config)
                    write_message(
                        pipe_handle,
                        {
                            "type": "hello_ack",
                            "protocolVersion": 1,
                            "ready": True,
                            "runtimeLabel": runtime_label,
                            "taskFileExists": task_path.exists(),
                        },
                    )
                    continue

                if message_type == "ping":
                    write_message(
                        pipe_handle,
                        {
                            "type": "pong",
                            "runtimeLabel": runtime_label,
                        },
                    )
                    continue

                if message_type == "infer":
                    request_id = message.get("requestId", "")
                    frame_id = message.get("frameId", 0)
                    frame = message.get("frame") or {}
                    timestamp_ms = int(message.get("timestampMs", 0))
                    if landmarker is None:
                        raise RuntimeError("Landmarker is not initialized.")

                    frame_bgr = decode_frame(frame)
                    roi = message.get("roi")
                    task_frame, roi_offset = apply_roi(frame_bgr, roi)
                    mp_image = bgr_to_mp_image(task_frame)
                    result = landmarker.detect_for_video(mp_image, timestamp_ms)
                    payload = build_hand_payload(result, roi_offset, frame_bgr.shape[1], frame_bgr.shape[0])

                    write_message(
                        pipe_handle,
                        {
                            "type": "inference_result",
                            "requestId": request_id,
                            "frameId": frame_id,
                            "isSuccess": True,
                            "runtimeLabel": runtime_label,
                            "payload": payload,
                            "metadata": {
                                "workerKind": args.worker_kind,
                                "taskFilePath": str(task_path),
                            },
                        },
                    )
                    continue

                write_message(
                    pipe_handle,
                    {
                        "type": "inference_result",
                        "requestId": message.get("requestId", ""),
                        "frameId": message.get("frameId", 0),
                        "isSuccess": False,
                        "runtimeLabel": runtime_label,
                        "errorCode": "unsupported_message",
                        "message": f"Unsupported message type: {message_type}",
                    },
                )
    except KeyboardInterrupt:
        return 0
    except Exception as ex:
        sys.stderr.write(f"worker failure: {ex}\n")
        sys.stderr.flush()
        return 1
    finally:
        if landmarker is not None:
            landmarker.close()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
