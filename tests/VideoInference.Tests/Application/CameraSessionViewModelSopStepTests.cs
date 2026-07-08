using System.Reflection;
using Xunit;

namespace VideoInferenceDemo.Tests.Application;

[Collection("DbSession")]
public sealed class CameraSessionViewModelSopStepTests
{
    [Fact]
    public void OnAnalysisResult_MarksCurrentStepNg_WhenResultHasNgReason()
    {
        using var context = new DesktopCoordinatorTestContext();
        var session = context.CreateSession();

        RaiseAnalysisResult(session, new AnalysisResult
        {
            StrategyName = AnalysisStrategyNames.Sop1,
            Step = 1,
            NgReason = "SOP1 step 1 detected product before inspection connector.",
            CurrentStateCode = "ng:sop1_product_before_inspection",
            ExpectedStateCode = "sop1:inspection_connector_ready",
            FrameIndex = 5,
            FrameUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        var first = Assert.Single(session.FsmStepSnapshots, step => step.Step == 1);
        var second = Assert.Single(session.FsmStepSnapshots, step => step.Step == 2);
        Assert.Equal(FsmStepStatus.Done, first.Status);
        Assert.True(first.IsNg);
        Assert.Equal(FsmStepStatus.Waiting, second.Status);
        Assert.False(second.IsNg);
        Assert.Equal("NG", session.SopOutcomeText);
    }

    private static void RaiseAnalysisResult(CameraSessionViewModel session, AnalysisResult result)
    {
        var method = typeof(CameraSessionViewModel).GetMethod(
            "OnAnalysisResult",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(session, new object[] { result });
    }
}
