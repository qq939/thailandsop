using SqlSugar;

namespace VideoInferenceDemo;

[SugarTable("camera_profiles")]
public sealed class CameraProfileEntity
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public string Id { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "enabled")]
    public int Enabled { get; set; }

    [SugarColumn(ColumnName = "auto_start")]
    public int AutoStart { get; set; }

    [SugarColumn(ColumnName = "provider_id")]
    public string ProviderId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "camera_index")]
    public int CameraIndex { get; set; }

    [SugarColumn(ColumnName = "device_id")]
    public string DeviceId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "opencv_source")]
    public string OpenCvSource { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "opencv_backend")]
    public string OpenCvBackend { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "trigger_mode")]
    public string TriggerMode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "rotation")]
    public string Rotation { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "mirror_mode")]
    public string MirrorMode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "target_fps")]
    public double TargetFps { get; set; }

    [SugarColumn(ColumnName = "use_source_pts_for_video")]
    public int UseSourcePtsForVideo { get; set; }

    [SugarColumn(ColumnName = "primary_task_id")]
    public string PrimaryTaskId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "selected_sop_profile_id")]
    public string SelectedSopProfileId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "enable_sop_analysis")]
    public int EnableSopAnalysis { get; set; }

    [SugarColumn(ColumnName = "analysis_frame_window_size")]
    public int AnalysisFrameWindowSize { get; set; }

    [SugarColumn(ColumnName = "analysis_state_window_size")]
    public int AnalysisStateWindowSize { get; set; }

    [SugarColumn(ColumnName = "analysis_hold_frames")]
    public int AnalysisHoldFrames { get; set; }

    [SugarColumn(ColumnName = "sop_window_ms")]
    public int SopWindowMs { get; set; }

    [SugarColumn(ColumnName = "sop_min_score_q1000")]
    public int SopMinScoreQ1000 { get; set; }

    [SugarColumn(ColumnName = "sop_min_visible_ratio_q1000")]
    public int SopMinVisibleRatioQ1000 { get; set; }

    [SugarColumn(ColumnName = "ocr_enabled")]
    public int OcrEnabled { get; set; }

    [SugarColumn(ColumnName = "ocr_roi_x")]
    public int OcrRoiX { get; set; }

    [SugarColumn(ColumnName = "ocr_roi_y")]
    public int OcrRoiY { get; set; }

    [SugarColumn(ColumnName = "ocr_roi_width")]
    public int OcrRoiWidth { get; set; }

    [SugarColumn(ColumnName = "ocr_roi_height")]
    public int OcrRoiHeight { get; set; }

    [SugarColumn(ColumnName = "enable_camera_recording")]
    public int EnableCameraRecording { get; set; }

    [SugarColumn(ColumnName = "recording_root_directory")]
    public string RecordingRootDirectory { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "recording_segment_minutes")]
    public int RecordingSegmentMinutes { get; set; }

    [SugarColumn(ColumnName = "recording_container_format")]
    public string RecordingContainerFormat { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "recording_video_encoder")]
    public string RecordingVideoEncoder { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "recording_codec_fourcc")]
    public string RecordingCodecFourcc { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "recording_queue_capacity")]
    public int RecordingQueueCapacity { get; set; }

    [SugarColumn(ColumnName = "recording_bitrate_mbps")]
    public int RecordingBitrateMbps { get; set; }

    [SugarColumn(ColumnName = "recording_fps")]
    public double RecordingFps { get; set; }

    [SugarColumn(ColumnName = "sort_order")]
    public int SortOrder { get; set; }

    [SugarColumn(ColumnName = "created_utc_ms")]
    public long CreatedUtcMs { get; set; }

    [SugarColumn(ColumnName = "updated_utc_ms")]
    public long UpdatedUtcMs { get; set; }
}
