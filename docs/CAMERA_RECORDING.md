# Camera Recording Notes

This document describes the camera recording pipeline and configuration.

## Overview

- Recording is enabled only for camera sources.
- Recording runs on its own worker queue so encoding does not block inference work.
- Output is segmented by configured minutes and rotates automatically when the local date changes.
- Folder and file naming:
  - Folder: `RootDirectory/yyyy-MM-dd/CameraName`
  - File: `yyyyMMdd_HHmmss.mkv`
- The current implementation targets `hevc_nvenc` with `.mkv`.

## Config file

Use `camera_config.json`:

```json
{
  "EnableCameraRecording": false,
  "RecordingRootDirectory": "Recordings",
  "RecordingSegmentMinutes": 5,
  "RecordingContainerFormat": "mkv",
  "RecordingVideoEncoder": "hevc_nvenc",
  "RecordingQueueCapacity": 120,
  "RecordingBitrateMbps": 8
}
```

## Field behavior

- `EnableCameraRecording`
  - `true`: start recorder when camera stream starts
  - `false`: do not record
- `RecordingRootDirectory`
  - relative paths are resolved from `AppContext.BaseDirectory`
- `RecordingSegmentMinutes`
  - rotate file when elapsed segment duration is reached
- `RecordingContainerFormat`
  - container extension, currently `mkv` or `mp4`
- `RecordingVideoEncoder`
  - FFmpeg encoder name, default `hevc_nvenc`
- `RecordingQueueCapacity`
  - bounded queue size; when full, recorder frames are dropped and the pipeline keeps running
- `RecordingBitrateMbps`
  - target encoder bitrate in Mbps

## Time handling

- Each camera driver is responsible for producing standardized `PTS` in milliseconds.
- `OpenCvCameraProvider`
  - falls back to a local monotonic clock
- `HikCameraProvider`
  - prefers device timestamp normalization and falls back to a local monotonic clock when device timing is not usable
- Recorder files use standardized `PTS`; database rows keep `PTS` and capture UTC separately.

## Coupling

- `CameraProfile` stores per-camera recording configuration.
- `VideoPipeline` owns recorder lifecycle but depends only on `IFrameRecorder`.
- `SegmentedVideoRecorder` only receives `Mat + ptsMs + captureUtcMs`.
- Recorder errors are propagated through the pipeline `Error` event.
- Business logic can request an immediate segment rotation through `VideoPipeline.RequestRecordingRotate()`.
