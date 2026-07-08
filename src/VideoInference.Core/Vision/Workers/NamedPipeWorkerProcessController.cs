using System.Diagnostics;

namespace VideoInferenceDemo;

internal sealed class NamedPipeWorkerProcessController : IDisposable
{
    private readonly PythonProcessLauncher _processLauncher;
    private readonly EventHandler _onExited;
    private readonly DataReceivedEventHandler _onErrorDataReceived;
    private Process? _process;

    public NamedPipeWorkerProcessController(
        PythonProcessLauncher processLauncher,
        EventHandler onExited,
        DataReceivedEventHandler onErrorDataReceived)
    {
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
        _onExited = onExited ?? throw new ArgumentNullException(nameof(onExited));
        _onErrorDataReceived = onErrorDataReceived ?? throw new ArgumentNullException(nameof(onErrorDataReceived));
    }

    public bool IsRunning => _process is { HasExited: false };
    public int? ActiveProcessId => _process?.HasExited == false ? _process.Id : null;

    public int? TryGetExitCode()
    {
        return _process?.HasExited == true ? _process.ExitCode : null;
    }

    public void Start(WorkerProcessStartSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        Stop();
        _process = _processLauncher.Start(spec);
        _process.EnableRaisingEvents = true;
        _process.Exited += _onExited;
        _process.ErrorDataReceived += _onErrorDataReceived;
        _process.BeginErrorReadLine();
    }

    public void Stop()
    {
        if (_process == null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(1000);
            }
        }
        catch
        {
        }
        finally
        {
            ClearSubscriptions();
            _process.Dispose();
            _process = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private void ClearSubscriptions()
    {
        if (_process == null)
        {
            return;
        }

        _process.ErrorDataReceived -= _onErrorDataReceived;
        _process.Exited -= _onExited;
    }
}
