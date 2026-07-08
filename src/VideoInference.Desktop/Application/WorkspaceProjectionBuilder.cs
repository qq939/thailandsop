using System.Collections.ObjectModel;
using System.Windows.Media;

namespace VideoInferenceDemo;

public sealed class WorkspaceProjectionBuilder
{
    public WorkspaceProjectionSnapshot Build(WorkspaceProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var currentSession = request.CurrentSession;
        var workspaceState = currentSession?.WorkspaceState;
        var workspaceStatus = workspaceState?.Status ?? request.WorkspaceStatusSnapshot;
        var isVideoSource = workspaceState?.IsVideoSource ?? currentSession?.IsVideoSource ?? request.IsVideoSource;
        var isRunning = workspaceState?.IsRunning ?? currentSession?.IsRunning ?? request.IsRunning;
        var isPaused = workspaceState?.IsPaused ?? currentSession?.IsPaused ?? request.IsPaused;
        var controlSourceText = workspaceState != null
            ? (workspaceState.IsVideoSource
                ? $"视频回放 / {workspaceState.CurrentVideoLabel}"
                : $"实时采集 / {workspaceState.SessionName}")
            : request.ControlSourceText;
        var controlHintText = workspaceState != null
            ? (workspaceState.IsVideoSource
                ? "当前 session 使用视频文件作为输入源，推理、后处理和 SOP 链路保持不变。"
                : "当前 session 使用相机作为输入源，录像、推理和 SOP 状态彼此独立。")
            : request.ControlHintText;
        var workspaceFsmSteps = currentSession?.FsmSteps
            ?? (workspaceState != null
            ? BuildWorkspaceFsmSteps(workspaceState.FsmSteps)
            : request.FsmSteps);
        var productionOkCount = currentSession?.ProductionOkCount ?? 0;
        var productionNgCount = currentSession?.ProductionNgCount ?? 0;
        var productionTotalCount = productionOkCount + productionNgCount;
        var productionYieldPercent = productionTotalCount > 0
            ? productionOkCount * 100d / productionTotalCount
            : 0d;
        var productionYieldText = productionTotalCount > 0
            ? $"{productionYieldPercent:0.00}%"
            : "-";
        var productionWorkDurationText = currentSession?.ProductionWorkDurationText
            ?? NormalizeProductionDuration(workspaceState?.Metrics.PlaybackTimeText ?? request.PlaybackTimeDisplay);
        var productionEmployeeText = currentSession?.ProductionEmployeeText ?? "未绑定";
        var productionLastUpdateText = currentSession?.ProductionLastUpdateText
            ?? (workspaceState?.Metrics.CurrentTimeText ?? request.CurrentTimeText);
        if (!string.IsNullOrWhiteSpace(request.ActiveBindingEmployeeName))
        {
            productionEmployeeText = request.ActiveBindingEmployeeName;
        }
        else if (request.SelectedPersonnel != null)
        {
            productionEmployeeText = request.CurrentSessionPersonnelDisplayText;
        }

        return new WorkspaceProjectionSnapshot(
            currentSession?.VideoFrame ?? request.VideoFrame,
            currentSession?.StatusText ?? request.StatusText,
            workspaceState?.SourceLabel ?? currentSession?.SourceLabel ?? request.SourceLabel,
            workspaceState?.InferenceStatus ?? currentSession?.InferenceStatus ?? request.InferenceStatus,
            workspaceState?.InferenceDeviceText ?? currentSession?.InferenceDeviceText ?? request.InferenceDeviceText,
            workspaceState?.LastFrameInfo ?? currentSession?.LastFrameInfo ?? request.LastFrameInfo,
            workspaceState?.LastError ?? currentSession?.LastError ?? request.LastError,
            GetTransitionStatusText(workspaceStatus),
            GetStatusBadgeText(workspaceStatus),
            controlSourceText,
            GetRunStateText(workspaceStatus),
            controlHintText,
            isPaused ? "继续" : "播放",
            workspaceState?.TargetFpsDisplay ?? currentSession?.TargetFpsDisplay ?? request.TargetFpsDisplay,
            workspaceState?.Metrics.CaptureFps ?? currentSession?.CaptureFps ?? request.CaptureFps,
            workspaceState?.Metrics.SourceFpsText ?? currentSession?.SourceFpsDisplay ?? request.SourceFpsDisplay,
            workspaceState?.Metrics.SourceDurationText ?? currentSession?.SourceDurationDisplay ?? request.SourceDurationDisplay,
            workspaceState?.Metrics.PlaybackTimeText ?? currentSession?.PlaybackTimeDisplay ?? request.PlaybackTimeDisplay,
            workspaceState?.Metrics.CurrentTimeText ?? currentSession?.CurrentTimeText ?? request.CurrentTimeText,
            workspaceState?.Metrics.DroppedByPts ?? currentSession?.DroppedByPts ?? request.DroppedByPts,
            workspaceState?.Metrics.DroppedByCaptureQueue ?? currentSession?.DroppedByCaptureQueue ?? request.DroppedByCaptureQueue,
            workspaceState?.Metrics.DroppedByInferDrain ?? currentSession?.DroppedByInferDrain ?? request.DroppedByInferDrain,
            workspaceState?.Metrics.DroppedByRenderQueue ?? currentSession?.DroppedByRenderQueue ?? request.DroppedByRenderQueue,
            workspaceState?.Metrics.DroppedByRenderDrain ?? currentSession?.DroppedByRenderDrain ?? request.DroppedByRenderDrain,
            productionOkCount,
            productionNgCount,
            productionYieldText,
            productionYieldPercent,
            productionWorkDurationText,
            productionEmployeeText,
            productionLastUpdateText,
            workspaceFsmSteps,
            isVideoSource,
            isRunning,
            isPaused,
            isVideoSource || !request.HasCameraSessions,
            !isVideoSource && request.HasCameraSessions,
            isVideoSource && request.HasCameraSessions);
    }

    private static ObservableCollection<FsmStepItem> BuildWorkspaceFsmSteps(IReadOnlyList<FsmStepSnapshot> snapshots)
    {
        var timelineOriginUtc = FsmStepItem.ResolveTimelineOrigin(snapshots);
        return new ObservableCollection<FsmStepItem>(
            snapshots.Select(snapshot => FsmStepItem.FromSnapshot(snapshot, timelineOriginUtc)));
    }

    private static string GetRunStateText(SessionStatusSnapshot snapshot)
    {
        return SessionStatusTextFormatter.GetRunStateText(snapshot);
    }

    private static string GetTransitionStatusText(SessionStatusSnapshot snapshot)
    {
        return SessionStatusTextFormatter.GetTransitionStatusText(snapshot);
    }

    private static string GetStatusBadgeText(SessionStatusSnapshot snapshot)
    {
        return SessionStatusTextFormatter.GetStatusBadgeText(snapshot);
    }

    private static string NormalizeProductionDuration(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || source == "-")
        {
            return "-";
        }

        var normalized = source.Trim();
        var dot = normalized.IndexOf('.');
        if (dot >= 0)
        {
            normalized = normalized[..dot];
        }

        var parts = normalized.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            return $"00:{parts[0]}:{parts[1]}";
        }

        return parts.Length == 3 ? normalized : "-";
    }
}

public sealed record WorkspaceProjectionRequest(
    CameraSessionViewModel? CurrentSession,
    SessionStatusSnapshot WorkspaceStatusSnapshot,
    ImageSource? VideoFrame,
    string StatusText,
    string SourceLabel,
    string InferenceStatus,
    string InferenceDeviceText,
    string LastFrameInfo,
    string LastError,
    string ControlSourceText,
    string ControlHintText,
    string TargetFpsDisplay,
    double CaptureFps,
    string SourceFpsDisplay,
    string SourceDurationDisplay,
    string PlaybackTimeDisplay,
    string CurrentTimeText,
    long DroppedByPts,
    long DroppedByCaptureQueue,
    long DroppedByInferDrain,
    long DroppedByRenderQueue,
    long DroppedByRenderDrain,
    ObservableCollection<FsmStepItem> FsmSteps,
    bool IsVideoSource,
    bool IsRunning,
    bool IsPaused,
    bool HasCameraSessions,
    PersonnelOptionItem? SelectedPersonnel,
    string CurrentSessionPersonnelDisplayText,
    string? ActiveBindingEmployeeName);
