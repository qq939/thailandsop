using System.Collections.ObjectModel;

namespace VideoInferenceDemo;

public sealed record CameraWorkspaceLoadResult(
    CameraSettings CameraSettings,
    CameraSessionViewModel? SelectedSession,
    bool AutoStartedSessions);

public sealed record InteractiveSessionLaunchResult(
    bool Success,
    CameraSessionViewModel? Session,
    string? StatusText = null,
    string? InferenceStatus = null,
    string? LastError = null);

public sealed record CameraSessionStartPreparationResult(
    CameraSessionViewModel? SelectedSession,
    SessionStartPrecheckResult PrecheckResult);

public sealed record CameraSessionWorkspaceRebuildResult(
    CameraSessionViewModel? SelectedSession);

public sealed record CameraSessionWorkspaceSelectionState(
    ObservableCollection<FsmStepItem> FsmSteps);
