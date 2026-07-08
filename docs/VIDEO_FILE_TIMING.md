# Video File Timing

## Background

The pipeline previously used `Target FPS` for two different jobs at the same time:

- sampling frames for inference
- rebuilding the playback clock for the preview window

This worked for simple CFR files, but it breaks on VFR files or files with incomplete FPS metadata.

Example observed on `O:\舍弗勒\A\A1.mkv`:

- container duration: about 25.224 s
- video stream duration: about 24.712 s
- `avg_frame_rate = 0/0`
- `r_frame_rate = 60/1`
- actual decoded frame count: 330
- average frame interval from frame PTS: about 75.06 ms
- actual average source rate from PTS: about 13.32 fps

If the pipeline samples those 330 frames at `Target FPS = 30` and also plays them back at 30 fps, the preview finishes in about 11 s instead of about 24.7 s.

## New timing model

Video-file mode now separates two time domains:

- `TimelineMs`: the preview/playback timeline
- `TimestampMs`: the frame timestamp written into downstream results

### Video files

- Frame selection for inference still uses `Target FPS`.
- Accepted frames are still the only frames rendered to the preview.
- Preview pacing is driven by the accepted frame's source PTS timeline.
- Result timestamps can still be configured:
  - `UseSourcePtsForVideo = true`: write source PTS into results
  - `UseSourcePtsForVideo = false`: write host monotonic time into results

This keeps detection boxes aligned with the displayed frame while preserving the original playback speed of the source video.

### Cameras

- Camera mode keeps the existing host-clock behavior.
- `Target FPS` continues to control runtime pacing.

## Practical effect

For video files:

- lowering `Target FPS` reduces inference cost
- displayed frames still match the boxes exactly
- playback duration stays close to the original file duration
- playback no longer speeds up just because inference is configured to a higher FPS than the source timeline supports

## Notes

- `Source FPS` in the UI is updated from observed source PTS while decoding video files, so VFR files converge toward a useful average instead of only showing container-declared metadata.
- The config checkbox text was clarified to indicate that it affects video timestamps, not the preview playback clock.
