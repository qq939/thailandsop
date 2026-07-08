namespace VideoInferenceDemo;

public sealed class WorkspaceSelectionCoordinator
{
    private readonly SessionTaskOrchestrator _sessionTaskOrchestrator;

    public WorkspaceSelectionCoordinator(SessionTaskOrchestrator sessionTaskOrchestrator)
    {
        _sessionTaskOrchestrator = sessionTaskOrchestrator ?? throw new ArgumentNullException(nameof(sessionTaskOrchestrator));
    }

    public WorkspaceSelectionMaterializationResult MaterializeSelection(
        VisionWorkspaceService workspace,
        IReadOnlyCollection<CameraSessionViewModel> sessions,
        VisionTaskCreationContext taskContext,
        WorkspaceSelectionMode mode,
        bool isVideoSource)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(taskContext);

        return MaterializePrimaryTaskSelection(
            workspace,
            sessions,
            taskContext,
            ResolveSelectionMode(workspace, mode),
            isVideoSource);
    }

    private WorkspaceSelectionMaterializationResult MaterializePrimaryTaskSelection(
        VisionWorkspaceService workspace,
        IReadOnlyCollection<CameraSessionViewModel> sessions,
        VisionTaskCreationContext taskContext,
        WorkspaceSelectionMode selectionMode,
        bool isVideoSource)
    {
        var snapshot = workspace.CurrentSnapshot;
        var task = snapshot.SelectedPrimaryTask;
        if (task == null)
        {
            return CreateNoSelectionResult(workspace);
        }

        return ShouldMaterializeFromModelSource(selectionMode, snapshot)
            ? MaterializeModelBackedBinding(workspace, sessions, taskContext, isVideoSource)
            : MaterializeTaskBinding(workspace, sessions, taskContext, task);
    }

    private WorkspaceSelectionMaterializationResult MaterializeTaskBinding(
        VisionWorkspaceService workspace,
        IReadOnlyCollection<CameraSessionViewModel> sessions,
        VisionTaskCreationContext taskContext,
        VisionTaskDefinition task)
    {
        try
        {
            var updatedSnapshot = workspace.ClearActivatedModelSource();
            var application = CreatePrimaryTaskApplication(task, taskContext);

            return ApplyBindingToSessions(
                sessions,
                updatedSnapshot,
                application.Binding,
                application.StatusText,
                application.InferenceStatus,
                application.InferenceDeviceText);
        }
        catch (Exception ex)
        {
            return CreateFailureResult(
                workspace.CurrentSnapshot,
                SessionStartPrecheckState.Error,
                "Error",
                $"Task apply failed: {ex.Message}",
                "-",
                ex.Message);
        }
    }

    private WorkspaceSelectionMaterializationResult MaterializeModelBackedBinding(
        VisionWorkspaceService workspace,
        IReadOnlyCollection<CameraSessionViewModel> sessions,
        VisionTaskCreationContext taskContext,
        bool isVideoSource)
    {
        var activation = workspace.TryMaterializePrimaryTaskModelSourceBinding(taskContext);
        var attempt = activation.Attempt;
        var activationStatus = ModelActivationStatusInfo.FromAttempt(attempt);

        if (!attempt.Success || attempt.Activation?.Binding == null)
        {
            return CreateFailureResult(
                activation.WorkspaceSnapshot,
                attempt.State == ModelActivationState.NoModel
                    ? SessionStartPrecheckState.NoModel
                    : SessionStartPrecheckState.Error,
                SessionStatusTextFormatter.GetRunStateText(attempt.ToStatusSnapshot(isVideoSource)),
                activationStatus.Detail,
                activationStatus.DeviceText,
                attempt.Message,
                attempt);
        }

        return ApplyBindingToSessions(
            sessions,
            activation.WorkspaceSnapshot,
            attempt.Activation.Binding,
            SessionStatusTextFormatter.GetRunStateText(attempt.ToStatusSnapshot(isVideoSource)),
            activationStatus.Detail,
            activationStatus.DeviceText,
            attempt,
            attempt.Message);
    }

    private VisionTaskApplicationResult CreatePrimaryTaskApplication(
        VisionTaskDefinition task,
        VisionTaskCreationContext taskContext)
    {
        var binding = _sessionTaskOrchestrator.CreatePrimaryTaskBinding(task, taskContext);
        return new VisionTaskApplicationResult(
            binding,
            "Task Selected",
            $"{task.DisplayName} ready",
            GetTaskDeviceText(task, taskContext));
    }

    private WorkspaceSelectionMaterializationResult ApplyBindingToSessions(
        IReadOnlyCollection<CameraSessionViewModel> sessions,
        VisionWorkspaceSnapshot snapshot,
        PrimaryVisionTaskBinding binding,
        string statusText,
        string inferenceStatus,
        string inferenceDeviceText,
        ModelActivationAttemptResult? modelBackedActivationAttempt = null,
        string? lastError = null)
    {
        _sessionTaskOrchestrator.ApplyPrimaryTaskBindingToAllSessions(binding, sessions);
        return CreateSuccessResult(
            snapshot,
            binding,
            modelBackedActivationAttempt,
            statusText,
            inferenceStatus,
            inferenceDeviceText,
            lastError);
    }

    private WorkspaceSelectionMode ResolveSelectionMode(VisionWorkspaceService workspace, WorkspaceSelectionMode preferredMode)
    {
        var snapshot = workspace.CurrentSnapshot;
        return preferredMode switch
        {
            WorkspaceSelectionMode.Auto => snapshot.HasPrimaryTask
                ? WorkspaceSelectionMode.Auto
                : WorkspaceSelectionMode.ModelBackedPrimaryTask,
            WorkspaceSelectionMode.PrimaryTask => snapshot.SelectedPrimaryTask != null
                ? WorkspaceSelectionMode.PrimaryTask
                : WorkspaceSelectionMode.ModelBackedPrimaryTask,
            WorkspaceSelectionMode.ModelBackedPrimaryTask => WorkspaceSelectionMode.ModelBackedPrimaryTask,
            _ => snapshot.HasPrimaryTask
                ? WorkspaceSelectionMode.Auto
                : WorkspaceSelectionMode.ModelBackedPrimaryTask
        };
    }

    private static bool ShouldMaterializeFromModelSource(
        WorkspaceSelectionMode selectionMode,
        VisionWorkspaceSnapshot snapshot)
    {
        return selectionMode != WorkspaceSelectionMode.PrimaryTask &&
               snapshot.IsModelBackedPrimaryTask;
    }

    internal static string GetTaskDeviceText(VisionTaskDefinition definition, VisionTaskCreationContext context)
    {
        return definition.RuntimeKind switch
        {
            VisionRuntimeKind.MediaPipe => "MediaPipe / Runtime",
            VisionRuntimeKind.OcrRuntime => "OCR / Runtime",
            _ => context.OnnxDeviceKind == InferenceDeviceKind.Cpu
                ? "OnnxRuntime / CPU"
                : "OnnxRuntime / GPU (Auto)"
        };
    }

    private static WorkspaceSelectionMaterializationResult CreateSuccessResult(
        VisionWorkspaceSnapshot snapshot,
        PrimaryVisionTaskBinding? binding,
        ModelActivationAttemptResult? modelBackedActivationAttempt,
        string statusText,
        string inferenceStatus,
        string inferenceDeviceText,
        string? lastError = null)
    {
        return new WorkspaceSelectionMaterializationResult(
            true,
            SessionStartPrecheckState.Ok,
            snapshot,
            binding,
            modelBackedActivationAttempt,
            statusText,
            inferenceStatus,
            inferenceDeviceText,
            lastError);
    }

    private static WorkspaceSelectionMaterializationResult CreateFailureResult(
        VisionWorkspaceSnapshot snapshot,
        SessionStartPrecheckState failureState,
        string statusText,
        string inferenceStatus,
        string inferenceDeviceText,
        string? lastError = null,
        ModelActivationAttemptResult? modelBackedActivationAttempt = null)
    {
        return new WorkspaceSelectionMaterializationResult(
            false,
            failureState,
            snapshot,
            null,
            modelBackedActivationAttempt,
            statusText,
            inferenceStatus,
            inferenceDeviceText,
            lastError);
    }

    private static WorkspaceSelectionMaterializationResult CreateNoSelectionResult(VisionWorkspaceService workspace)
    {
        return CreateFailureResult(
            workspace.CurrentSnapshot,
            SessionStartPrecheckState.NoModel,
            "No Model",
            "Please select a task or model first.",
            "-",
            "No task or model is currently available.");
    }
}
