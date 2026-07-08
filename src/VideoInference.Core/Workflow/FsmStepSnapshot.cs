using System;

namespace VideoInferenceDemo;

public sealed record FsmStepSnapshot(
    int Step,
    string Name,
    string? ActionCode,
    string? TcnLabel,
    string? ExpectedStateCode,
    FsmStepStatus Status,
    DateTimeOffset? StartTimeUtc,
    DateTimeOffset? EndTimeUtc,
    TimeSpan? Duration,
    bool IsNg)
{
    public static FsmStepSnapshot FromDefinition(FsmStepDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new FsmStepSnapshot(
            definition.Step,
            definition.Name,
            definition.ActionCode,
            definition.TcnLabel,
            definition.ExpectedStateCode,
            FsmStepStatus.Waiting,
            null,
            null,
            null,
            false);
    }
}
