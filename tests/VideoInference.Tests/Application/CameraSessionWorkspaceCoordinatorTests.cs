using System.Collections.ObjectModel;
using System.ComponentModel;
using Xunit;

namespace VideoInferenceDemo.Tests.Application;

[Collection("DbSession")]
public sealed class CameraSessionWorkspaceCoordinatorTests
{
    [Fact]
    public void RebuildSessions_CreatesSessionsAndSelectsConfiguredCamera()
    {
        using var context = new DesktopCoordinatorTestContext();
        var sessions = new ObservableCollection<CameraSessionViewModel>();
        var first = context.CreateCameraProfile(id: "camera-a", name: "Camera A");
        var second = context.CreateCameraProfile(id: "camera-b", name: "Camera B");
        var settings = new CameraSettings
        {
            Cameras = new List<CameraProfile> { first, second },
            SelectedCameraId = second.Id
        };

        var result = context.CameraSessionWorkspaceCoordinator.RebuildSessions(
            sessions,
            settings,
            context.AnalysisConfig,
            context.DefaultSopProfiles,
            null,
            null,
            DesktopCoordinatorTestContext.CreateTaskContext(),
            static _ => SessionStartPrecheckResult.Success,
            static (_, _) => { });

        Assert.Equal(2, sessions.Count);
        Assert.Equal(second.Id, result.SelectedSession?.Id);

        foreach (var session in sessions)
        {
            session.Dispose();
        }
    }

    [Fact]
    public void RebuildSessions_EnablesSopAnalysis_WhenGlobalOnlineAnalysisIsDisabled()
    {
        using var context = new DesktopCoordinatorTestContext();
        var sessions = new ObservableCollection<CameraSessionViewModel>();
        var profile = context.CreateCameraProfile(id: "camera-a", name: "Camera A");
        profile.PrimaryTaskId = "detector-a";
        profile.SelectedSopProfileId = "sop-default";
        var settings = new CameraSettings
        {
            Cameras = new List<CameraProfile> { profile },
            SopProfiles = context.DefaultSopProfiles.Select(profile => new CameraSopProfile
            {
                Id = profile.Id,
                Name = profile.Name,
                Strategy = profile.Strategy,
                FingerprintModuleId = profile.FingerprintModuleId,
                Steps = profile.Steps.Select(step => new CameraSopStep
                {
                    Step = step.Step,
                    Name = step.Name,
                    ActionCode = step.ActionCode,
                    TcnLabel = step.TcnLabel,
                    ExpectedStateCode = step.ExpectedStateCode
                }).ToList()
            }).ToList(),
            SelectedCameraId = profile.Id
        };
        var analysisConfig = new AnalysisConfig
        {
            EnableOnlineAnalysis = false,
            Strategy = AnalysisStrategyNames.SopRules,
            FrameWindowSize = 100,
            StateWindowSize = 30,
            NearThresholdQ1000 = 300,
            SopWindowMs = 1500,
            SopMinScoreQ1000 = 450,
            SopMinVisibleRatioQ1000 = 600
        };

        context.CameraSessionWorkspaceCoordinator.RebuildSessions(
            sessions,
            settings,
            analysisConfig,
            context.DefaultSopProfiles,
            null,
            null,
            DesktopCoordinatorTestContext.CreateTaskContext(),
            static _ => SessionStartPrecheckResult.Success,
            static (_, _) => { });

        var session = Assert.Single(sessions);
        Assert.True(session.IsAnalysisEnabled);
        session.Dispose();
    }

    [Fact]
    public void BuildSelectionState_UsesSelectedSessionSteps_WhenSessionExists()
    {
        using var context = new DesktopCoordinatorTestContext();
        var session = context.CreateSession();
        var fallbackSteps = new ObservableCollection<FsmStepItem>();

        var state = context.CameraSessionWorkspaceCoordinator.BuildSelectionState(session, fallbackSteps);

        Assert.Same(session.FsmSteps, state.FsmSteps);
    }

    [Fact]
    public void PrepareSessionStart_ReturnsFailurePrecheck_WhenMaterializationFails()
    {
        using var context = new DesktopCoordinatorTestContext();
        var session = context.CreateSession();
        var snapshot = new VisionWorkspaceSnapshot(
            new ModelWorkspaceSnapshot(Array.Empty<ModelCatalogEntry>(), null, null),
            Array.Empty<VisionTaskDefinition>(),
            null,
            VisionWorkspacePrimarySelectionKind.None,
            null,
            new ModelWorkspaceStatusInfo(ModelWorkspaceState.Empty, "Empty", "No models"),
            false);

        var result = context.CameraSessionWorkspaceCoordinator.PrepareSessionStart(
            session,
            () => new WorkspaceSelectionMaterializationResult(
                false,
                SessionStartPrecheckState.NoModel,
                snapshot,
                null,
                null,
                "No Model",
                "Select a task first.",
                "-",
                "Primary task missing."));

        Assert.Null(result.SelectedSession);
        Assert.False(result.PrecheckResult.IsSuccess);
        Assert.Equal(SessionStartPrecheckState.NoModel, result.PrecheckResult.State);
        Assert.Equal("Primary task missing.", result.PrecheckResult.Message);
    }

    [Fact]
    public void PrepareSessionStart_ReturnsSelectedSession_WhenMaterializationSucceeds()
    {
        using var context = new DesktopCoordinatorTestContext();
        var session = context.CreateSession();
        var snapshot = new VisionWorkspaceSnapshot(
            new ModelWorkspaceSnapshot(Array.Empty<ModelCatalogEntry>(), null, null),
            Array.Empty<VisionTaskDefinition>(),
            null,
            VisionWorkspacePrimarySelectionKind.None,
            null,
            new ModelWorkspaceStatusInfo(ModelWorkspaceState.Empty, "Empty", "No models"),
            false);

        var result = context.CameraSessionWorkspaceCoordinator.PrepareSessionStart(
            session,
            () => new WorkspaceSelectionMaterializationResult(
                true,
                SessionStartPrecheckState.Ok,
                snapshot,
                null,
                null,
                "Ready",
                "Task ready",
                "MediaPipe / Runtime"));

        Assert.Same(session, result.SelectedSession);
        Assert.True(result.PrecheckResult.IsSuccess);
    }

    [Fact]
    public void ResolvePreferredInteractiveSession_FallsBackToSelectedSession_WhenSettingsHaveNoCamera()
    {
        using var context = new DesktopCoordinatorTestContext();
        var selectedSession = context.CreateSession(context.CreateCameraProfile(id: "camera-a", name: "Camera A"));
        var otherSession = context.CreateSession(context.CreateCameraProfile(id: "camera-b", name: "Camera B"));
        var settings = new CameraSettings();

        var result = context.CameraSessionWorkspaceCoordinator.ResolvePreferredInteractiveSession(
            settings,
            selectedSession,
            new[] { otherSession, selectedSession });

        Assert.Same(selectedSession, result);
    }

    [Fact]
    public void StartPreferredInteractiveCamera_ReturnsNoCameraStatus_WhenNoSessionsExist()
    {
        using var context = new DesktopCoordinatorTestContext();

        var result = context.CameraSessionWorkspaceCoordinator.StartPreferredInteractiveCamera(
            new CameraSettings(),
            null,
            Array.Empty<CameraSessionViewModel>());

        Assert.False(result.Success);
        Assert.Equal("No Camera", result.StatusText);
        Assert.Equal("No camera profile is available.", result.LastError);
    }
}
