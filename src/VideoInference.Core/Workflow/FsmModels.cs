namespace VideoInferenceDemo;

public enum FsmStepStatus
{
    Waiting,
    InProgress,
    Done
}

public sealed class FsmStepDefinition
{
    public int Step { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ActionCode { get; set; }
    public string? TcnLabel { get; set; }
    public string? ExpectedStateCode { get; set; }
}
