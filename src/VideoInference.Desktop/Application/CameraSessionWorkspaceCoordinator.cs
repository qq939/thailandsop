using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace VideoInferenceDemo;

public sealed class CameraSessionWorkspaceCoordinator
{
    private readonly SessionTaskOrchestrator _sessionTaskOrchestrator;
    private readonly WorkspaceRunCoordinator _workspaceRunCoordinator;

    public CameraSessionWorkspaceCoordinator(
        SessionTaskOrchestrator sessionTaskOrchestrator,
        WorkspaceRunCoordinator workspaceRunCoordinator)
    {
        _sessionTaskOrchestrator = sessionTaskOrchestrator ?? throw new ArgumentNullException(nameof(sessionTaskOrchestrator));
        _workspaceRunCoordinator = workspaceRunCoordinator ?? throw new ArgumentNullException(nameof(workspaceRunCoordinator));
    }

    public CameraWorkspaceLoadResult LoadCameraWorkspace(
        CameraSettings cameraSettings,
        ObservableCollection<CameraSessionViewModel> sessions,
        AnalysisConfig analysisConfig,
        IReadOnlyList<SopProfile> sopProfiles,
        PrimaryVisionTaskBinding? activePrimaryBinding,
        VisionTaskDefinition? selectedPrimaryTask,
        VisionTaskCreationContext taskContext,
        Func<CameraSessionViewModel, SessionStartPrecheckResult> prepareStart,
        PropertyChangedEventHandler onCameraSessionPropertyChanged,
        bool isVideoSource,
        Func<CameraSessionViewModel, PrimaryVisionTaskBinding?>? getBinding = null)
    {
        ArgumentNullException.ThrowIfNull(cameraSettings);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(analysisConfig);
        ArgumentNullException.ThrowIfNull(sopProfiles);
        ArgumentNullException.ThrowIfNull(prepareStart);
        ArgumentNullException.ThrowIfNull(onCameraSessionPropertyChanged);

        var rebuildResult = RebuildSessions(
            sessions,
            cameraSettings,
            analysisConfig,
            ToSopProfiles(cameraSettings.SopProfiles),
            activePrimaryBinding,
            selectedPrimaryTask,
            taskContext,
            prepareStart,
            onCameraSessionPropertyChanged,
            getBinding);
        var autoStartedSessions = !isVideoSource &&
                                  _sessionTaskOrchestrator.StartConfiguredSessions(
                                      cameraSettings.GetAutoStartCameras(),
                                      sessions);

        return new CameraWorkspaceLoadResult(
            cameraSettings,
            rebuildResult.SelectedSession,
            autoStartedSessions);
    }

    public CameraSessionWorkspaceRebuildResult RebuildSessions(
        ObservableCollection<CameraSessionViewModel> sessions,
        CameraSettings cameraSettings,
        AnalysisConfig analysisConfig,
        IReadOnlyList<SopProfile> sopProfiles,
        PrimaryVisionTaskBinding? activePrimaryBinding,
        VisionTaskDefinition? selectedPrimaryTask,
        VisionTaskCreationContext taskContext,
        Func<CameraSessionViewModel, SessionStartPrecheckResult> prepareStart,
        PropertyChangedEventHandler onCameraSessionPropertyChanged,
        Func<CameraSessionViewModel, PrimaryVisionTaskBinding?>? getBinding = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(cameraSettings);
        ArgumentNullException.ThrowIfNull(analysisConfig);
        ArgumentNullException.ThrowIfNull(sopProfiles);
        ArgumentNullException.ThrowIfNull(prepareStart);
        ArgumentNullException.ThrowIfNull(onCameraSessionPropertyChanged);

        _workspaceRunCoordinator.ResetSessionBindings();
        var result = _sessionTaskOrchestrator.RehydrateSessions(
            sessions,
            cameraSettings,
            analysisConfig,
            ToSopProfiles(cameraSettings.SopProfiles),
            activePrimaryBinding,
            selectedPrimaryTask,
            taskContext,
            prepareStart,
            onCameraSessionPropertyChanged,
            getBinding);

        return new CameraSessionWorkspaceRebuildResult(result.SelectedSession);
    }

    public CameraSessionWorkspaceSelectionState BuildSelectionState(
        CameraSessionViewModel? selectedSession,
        ObservableCollection<FsmStepItem> videoFsmSteps)
    {
        ArgumentNullException.ThrowIfNull(videoFsmSteps);

        return new CameraSessionWorkspaceSelectionState(
            selectedSession?.FsmSteps ?? videoFsmSteps);
    }

    public CameraSessionStartPreparationResult PrepareSessionStart(
        CameraSessionViewModel session,
        Func<WorkspaceSelectionMaterializationResult> materializeSelection)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(materializeSelection);

        var materialization = materializeSelection();
        return materialization.Success
            ? new CameraSessionStartPreparationResult(session, SessionStartPrecheckResult.Success)
            : new CameraSessionStartPreparationResult(null, materialization.ToPrecheckResult());
    }

    public void ApplyFsmDefinitions(
        IEnumerable<CameraSessionViewModel> sessions,
        IReadOnlyList<FsmStepDefinition> fsmDefinitions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(fsmDefinitions);

        foreach (var session in sessions)
        {
            session.ApplyFsmDefinitions(fsmDefinitions);
        }
    }

    public InteractiveSessionLaunchResult StartPreferredInteractiveCamera(
        CameraSettings cameraSettings,
        CameraSessionViewModel? selectedSession,
        IReadOnlyCollection<CameraSessionViewModel> sessions)
    {
        ArgumentNullException.ThrowIfNull(cameraSettings);
        ArgumentNullException.ThrowIfNull(sessions);

        var session = ResolvePreferredInteractiveSession(cameraSettings, selectedSession, sessions);
        if (session == null)
        {
            return new InteractiveSessionLaunchResult(
                false,
                null,
                "No Camera",
                "Please configure at least one camera profile.",
                "No camera profile is available.");
        }

        _ = session.StartAsync();
        return new InteractiveSessionLaunchResult(true, session);
    }

    public InteractiveSessionLaunchResult OpenVideoOnPreferredSession(
        CameraSettings cameraSettings,
        CameraSessionViewModel? selectedSession,
        IReadOnlyCollection<CameraSessionViewModel> sessions,
        string path,
        double targetFps,
        bool useSourcePtsForVideo)
    {
        ArgumentNullException.ThrowIfNull(cameraSettings);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var session = ResolvePreferredInteractiveSession(cameraSettings, selectedSession, sessions);
        if (session == null)
        {
            return new InteractiveSessionLaunchResult(
                false,
                null,
                "No Session",
                "Please configure at least one session before opening video.",
                "No session is available for video playback.");
        }

        return session.OpenVideo(path, targetFps, useSourcePtsForVideo)
            ? new InteractiveSessionLaunchResult(true, session)
            : new InteractiveSessionLaunchResult(false, session);
    }

    public CameraSessionViewModel? ResolvePreferredInteractiveSession(
        CameraSettings cameraSettings,
        CameraSessionViewModel? selectedSession,
        IReadOnlyCollection<CameraSessionViewModel> sessions)
    {
        ArgumentNullException.ThrowIfNull(cameraSettings);
        ArgumentNullException.ThrowIfNull(sessions);

        var preferredProfile = cameraSettings.ResolvePreferredInteractiveCamera();
        return (preferredProfile != null
                   ? sessions.FirstOrDefault(camera =>
                       string.Equals(camera.Id, preferredProfile.Id, StringComparison.OrdinalIgnoreCase))
                   : null)
               ?? selectedSession
               ?? sessions.FirstOrDefault(camera => camera.Profile.Enabled)
               ?? sessions.FirstOrDefault();
    }

    public bool StartConfiguredSessions(
        IEnumerable<CameraProfile> profiles,
        IReadOnlyCollection<CameraSessionViewModel> sessions)
    {
        return _sessionTaskOrchestrator.StartConfiguredSessions(profiles, sessions);
    }

    private static IReadOnlyList<SopProfile> ToSopProfiles(IEnumerable<CameraSopProfile> profiles)
    {
        return profiles
            .Select(profile => new SopProfile
            {
                Id = profile.Id,
                Name = profile.Name,
                Strategy = profile.Strategy,
                FingerprintModuleId = profile.FingerprintModuleId,
                Steps = profile.Steps
                    .Select(step => new FsmStepDefinition
                    {
                        Step = step.Step,
                        Name = step.Name,
                        ActionCode = step.ActionCode,
                        TcnLabel = step.TcnLabel,
                        ExpectedStateCode = step.ExpectedStateCode
                    })
                    .ToList()
            })
            .ToList();
    }
}
