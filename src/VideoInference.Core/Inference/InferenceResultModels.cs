using System.Collections.Generic;

namespace VideoInferenceDemo;

public sealed class FrameEntity
{
    public long Id { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string SourceType { get; set; } = "video";
    public string RunUuid { get; set; } = string.Empty;
    public long RunStartedUtcMs { get; set; }
    public int FrameIndex { get; set; }
    public long TimestampMs { get; set; }
    public long FrameUtcMs { get; set; }
    public long InferenceTimeMs { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? ModelVersion { get; set; }
}

public sealed class DetectionEntity
{
    public long Id { get; set; }
    public long FrameId { get; set; }
    public int ClassId { get; set; }
    public string? ClassName { get; set; }
    public float Score { get; set; }
    public float X1 { get; set; }
    public float Y1 { get; set; }
    public float X2 { get; set; }
    public float Y2 { get; set; }
}

public sealed class FrameDetections
{
    public FrameDetections(FrameEntity frame, IReadOnlyList<DetectionEntity> detections)
        : this(frame, detections, string.Empty, VisionTaskKind.Detection)
    {
    }

    public FrameDetections(
        FrameEntity frame,
        IReadOnlyList<DetectionEntity> detections,
        string taskId,
        VisionTaskKind taskKind)
    {
        Frame = frame;
        Detections = detections;
        TaskId = taskId ?? string.Empty;
        TaskKind = taskKind;
    }

    public FrameEntity Frame { get; }
    public IReadOnlyList<DetectionEntity> Detections { get; }
    public string TaskId { get; }
    public VisionTaskKind TaskKind { get; }
}

public interface ILegacyDetectionResultSink
{
    bool TryEnqueue(FrameDetections batch);
}
