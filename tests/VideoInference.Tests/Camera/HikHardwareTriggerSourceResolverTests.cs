using System.Linq;
using VideoInferenceDemo;
using Xunit;

namespace VideoInference.Tests.Camera;

public sealed class HikHardwareTriggerSourceResolverTests
{
    [Fact]
    public void BuildCandidateOrder_SelectsFirstInputLine()
    {
        var candidates = HikHardwareTriggerSourceResolver.BuildCandidateOrder(
            [
                new HikLineModeProbe("Line0", "Output"),
                new HikLineModeProbe("Line1", "Input"),
                new HikLineModeProbe("Line2", "Input")
            ],
            ["Line0", "Line1", "Line2"]);

        Assert.Equal(["Line1", "Line2"], candidates);
    }

    [Fact]
    public void BuildCandidateOrder_FiltersInputLinesByTriggerSourceEntries()
    {
        var candidates = HikHardwareTriggerSourceResolver.BuildCandidateOrder(
            [
                new HikLineModeProbe("Line1", "Input"),
                new HikLineModeProbe("Line2", "Input")
            ],
            ["Software", "Line2"]);

        Assert.Equal(["Line2"], candidates);
    }

    [Fact]
    public void BuildCandidateOrder_UsesTriggerSourceEntriesWhenLineModeIsUnavailable()
    {
        var candidates = HikHardwareTriggerSourceResolver.BuildCandidateOrder(
            [],
            ["Software", "Line2", "Line1"]);

        Assert.Equal(["Line1", "Line2"], candidates);
    }

    [Fact]
    public void BuildCandidateOrder_ReturnsNoCandidatesWhenKnownLineModesHaveNoInput()
    {
        var candidates = HikHardwareTriggerSourceResolver.BuildCandidateOrder(
            [
                new HikLineModeProbe("Line0", "Output"),
                new HikLineModeProbe("Line1", "Output")
            ],
            ["Line0", "Line1"]);

        Assert.Empty(candidates);
    }

    [Fact]
    public void BuildCandidateOrder_FallsBackToDefaultLinesWithoutAnyCapabilityInfo()
    {
        var candidates = HikHardwareTriggerSourceResolver.BuildCandidateOrder([], []);

        Assert.Equal("Line0", candidates.First());
        Assert.Contains("Line7", candidates);
    }
}
