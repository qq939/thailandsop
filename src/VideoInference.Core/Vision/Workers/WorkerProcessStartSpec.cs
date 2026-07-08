using System.Collections.ObjectModel;

namespace VideoInferenceDemo;

public sealed class WorkerProcessStartSpec
{
    public required string FileName { get; init; }
    public string Arguments { get; init; } = string.Empty;
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
}
