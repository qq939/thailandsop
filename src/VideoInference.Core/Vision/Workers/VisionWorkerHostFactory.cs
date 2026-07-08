namespace VideoInferenceDemo;

public sealed class VisionWorkerHostFactory
{
    private readonly PythonProcessLauncher _pythonProcessLauncher;

    public VisionWorkerHostFactory(PythonProcessLauncher? pythonProcessLauncher = null)
    {
        _pythonProcessLauncher = pythonProcessLauncher ?? new PythonProcessLauncher();
    }

    public IVisionWorkerHost Create(VisionWorkerHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.ProtocolKind switch
        {
            VisionWorkerProtocolKind.NamedPipe => new NamedPipeVisionWorkerHost(options, _pythonProcessLauncher),
            _ => throw new NotSupportedException(
                $"Worker protocol '{options.ProtocolKind}' is not supported.")
        };
    }
}
