namespace VideoInferenceDemo;

public interface IEnvironmentDiagnosticsService
{
    EnvironmentDiagnosticsResult Run();
}

public sealed record EnvironmentDiagnosticsResult(
    EnvironmentDiagnosticsState State,
    string Summary,
    string ReportPath,
    EnvironmentDiagnosticsReport? Report = null)
{
    public bool Success => State != EnvironmentDiagnosticsState.Error;
}
