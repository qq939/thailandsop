using System.Diagnostics;

namespace VideoInferenceDemo;

public sealed class PythonProcessLauncher
{
    public Process Start(WorkerProcessStartSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (string.IsNullOrWhiteSpace(spec.FileName))
        {
            throw new ArgumentException("Worker process filename is required.", nameof(spec));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = spec.FileName,
            Arguments = spec.Arguments,
            WorkingDirectory = ResolveWorkingDirectory(spec),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var pair in spec.EnvironmentVariables)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        return Process.Start(startInfo)
               ?? throw new InvalidOperationException(
                   $"Failed to start worker process '{spec.FileName}'.");
    }

    private static string ResolveWorkingDirectory(WorkerProcessStartSpec spec)
    {
        if (!string.IsNullOrWhiteSpace(spec.WorkingDirectory))
        {
            return spec.WorkingDirectory;
        }

        var directory = Path.GetDirectoryName(spec.FileName);
        return string.IsNullOrWhiteSpace(directory)
            ? Environment.CurrentDirectory
            : directory;
    }
}
