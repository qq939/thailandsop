namespace VideoInferenceDemo.ImageInspection.Runtime;

public sealed record InspectionRuntimeTaskResult(
    bool Executed,
    InspectionCycleDecision Decision,
    string TriggerId,
    DateTimeOffset TriggerTime)
{
    public static InspectionRuntimeTaskResult NotStarted(string triggerId)
    {
        return new InspectionRuntimeTaskResult(
            false,
            InspectionCycleDecision.Unknown,
            triggerId,
            DateTimeOffset.Now);
    }
}
