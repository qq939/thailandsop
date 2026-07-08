using Xunit;

namespace VideoInferenceDemo.Tests.Application;

[Collection("DbSession")]
public sealed class WorkspaceSelectionCoordinatorTests
{
    [Fact]
    public void MaterializeSelection_ReturnsNoModelFailure_WhenNoPrimaryTaskExists()
    {
        using var context = new DesktopCoordinatorTestContext();
        var workspace = context.CreateWorkspaceService();
        workspace.ReloadCatalog();

        var result = context.WorkspaceSelectionCoordinator.MaterializeSelection(
            workspace,
            Array.Empty<CameraSessionViewModel>(),
            DesktopCoordinatorTestContext.CreateTaskContext(),
            WorkspaceSelectionMode.Auto,
            isVideoSource: false);

        Assert.False(result.Success);
        Assert.Equal(SessionStartPrecheckState.NoModel, result.FailureState);
        Assert.Equal("No Model", result.StatusText);
        Assert.Null(result.ActivePrimaryBinding);
    }

    [Fact]
    public void MaterializeSelection_ReturnsBinding_ForSpecialPrimaryTask()
    {
        using var context = new DesktopCoordinatorTestContext();
        context.CreateSpecialTaskBundle("hand-landmarks", "Hand Landmarks");
        var workspace = context.CreateWorkspaceService();
        workspace.ReloadCatalog(preferredTaskId: "hand-landmarks");

        var result = context.WorkspaceSelectionCoordinator.MaterializeSelection(
            workspace,
            Array.Empty<CameraSessionViewModel>(),
            DesktopCoordinatorTestContext.CreateTaskContext(),
            WorkspaceSelectionMode.PrimaryTask,
            isVideoSource: false);

        Assert.True(result.Success);
        Assert.NotNull(result.ActivePrimaryBinding);
        Assert.Equal("hand-landmarks", result.ActivePrimaryBinding!.Definition.Id);
        Assert.Equal("Task Selected", result.StatusText);
        Assert.Equal("Hand Landmarks ready", result.InferenceStatus);
        Assert.Equal("MediaPipe / Runtime", result.InferenceDeviceText);
    }

    [Fact]
    public void MaterializeSelection_ReturnsModelActivationFailure_WhenModelSourceDisappears()
    {
        using var context = new DesktopCoordinatorTestContext();
        var modelPath = context.CreateModelBundle("detector-a", "Detector A");
        var workspace = context.CreateWorkspaceService();
        workspace.ReloadCatalog(preferredModelId: "detector-a");
        File.Delete(modelPath);

        var result = context.WorkspaceSelectionCoordinator.MaterializeSelection(
            workspace,
            Array.Empty<CameraSessionViewModel>(),
            DesktopCoordinatorTestContext.CreateTaskContext(),
            WorkspaceSelectionMode.Auto,
            isVideoSource: false);

        Assert.False(result.Success);
        Assert.Equal(SessionStartPrecheckState.Error, result.FailureState);
        Assert.NotNull(result.ModelBackedActivationAttempt);
        Assert.Equal(ModelActivationState.Error, result.ModelBackedActivationAttempt!.State);
        Assert.Contains("Model file not found", result.LastError, StringComparison.Ordinal);
    }
}
