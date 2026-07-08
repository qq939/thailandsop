using System.Collections.ObjectModel;
using Xunit;

namespace VideoInferenceDemo.Tests.Application;

[Collection("DbSession")]
public sealed class CameraSessionWorkspaceCoordinatorConfigTests
{
    [Fact]
    public void ApplyFsmDefinitions_PropagatesDefinitionsToAllSessions()
    {
        using var context = new DesktopCoordinatorTestContext();
        var sessionA = context.CreateSession(context.CreateCameraProfile(id: "camera-a", name: "Camera A"));
        var sessionB = context.CreateSession(context.CreateCameraProfile(id: "camera-b", name: "Camera B"));
        var sessions = new ObservableCollection<CameraSessionViewModel> { sessionA, sessionB };
        var definitions = new[]
        {
            new FsmStepDefinition { Step = 1, Name = "Inspect" },
            new FsmStepDefinition { Step = 2, Name = "Verify" }
        };

        context.CameraSessionWorkspaceCoordinator.ApplyFsmDefinitions(sessions, definitions);

        Assert.Collection(
            sessionA.FsmStepSnapshots,
            step => Assert.Equal("Inspect", step.Name),
            step => Assert.Equal("Verify", step.Name));
        Assert.Collection(
            sessionB.FsmStepSnapshots,
            step => Assert.Equal("Inspect", step.Name),
            step => Assert.Equal("Verify", step.Name));
    }
}
