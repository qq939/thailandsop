using System;
using System.Collections.Generic;

namespace VideoInferenceDemo;

public sealed record SessionWorkspaceState(
    SessionStatusSnapshot Status,
    SessionMetricsSnapshot Metrics,
    string SessionName,
    string CurrentVideoLabel,
    string SourceLabel,
    string InferenceStatus,
    string InferenceDeviceText,
    string LastFrameInfo,
    string LastError,
    string TargetFpsDisplay,
    bool HasVideoPath,
    IReadOnlyList<FsmStepSnapshot> FsmSteps)
{
    public static SessionWorkspaceState Empty { get; } = new(
        SessionStatusSnapshot.Empty,
        SessionMetricsSnapshot.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        "-",
        "-",
        string.Empty,
        "-",
        false,
        Array.Empty<FsmStepSnapshot>());

    public bool IsVideoSource => Status.IsVideoSource;

    public bool IsRunning => Status.RunState is SessionRunState.Starting or SessionRunState.Running or SessionRunState.Paused;

    public bool IsPaused => Status.RunState == SessionRunState.Paused;
}
