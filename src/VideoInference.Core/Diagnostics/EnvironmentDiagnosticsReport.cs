namespace VideoInferenceDemo;

public enum DiagnosticsSeverity
{
    Info,
    Warning,
    Error
}

public sealed record DiagnosticsCheckResult(
    string Category,
    string Name,
    DiagnosticsSeverity Severity,
    string Message);

public sealed record EnvironmentDiagnosticsReport(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<DiagnosticsCheckResult> Checks)
{
    public bool Success => Checks.All(check => check.Severity != DiagnosticsSeverity.Error);
}
