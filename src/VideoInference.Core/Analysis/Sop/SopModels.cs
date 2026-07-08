using System;
using System.Collections.Generic;

namespace VideoInferenceDemo;

public readonly record struct SopBoundingBox(float X1, float Y1, float X2, float Y2);

public sealed record SopObjectInstance(
    int ClassId,
    string Label,
    float Score,
    SopBoundingBox Box);

public sealed record SopObjectWindowState(
    int ClassId,
    string Label,
    int TotalCount,
    int PresentFrameCount,
    int WindowFrameCount,
    float BestScore,
    SopBoundingBox? BestBox,
    IReadOnlyList<SopObjectInstance> Instances)
{
    public int VisibleRatioQ1000 => WindowFrameCount <= 0
        ? 0
        : Math.Clamp((int)Math.Round(PresentFrameCount * 1000.0 / WindowFrameCount, MidpointRounding.AwayFromZero), 0, 1000);
}

public sealed record SopWindowState(
    string SourceKey,
    string TaskId,
    VisionTaskKind TaskKind,
    long StartPtsMs,
    long EndPtsMs,
    IReadOnlyList<SopObjectWindowState> Objects)
{
    public static SopWindowState Empty(FsmFrameMetrics current) => new(
        current.SourceKey,
        current.TaskId,
        current.TaskKind,
        current.PtsMs,
        current.PtsMs,
        Array.Empty<SopObjectWindowState>());
}

public sealed record SopMatchedState(
    string StateCode,
    string? Label,
    double? Score,
    string? Note = null,
    SopObjectWindowState? Object = null);

public sealed record SopRuleContext(
    FsmFrameMetrics Current,
    AnalysisContext Analysis,
    SopWindowState Window);
