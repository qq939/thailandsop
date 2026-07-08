using System.Diagnostics;

namespace VideoInferenceDemo;

internal sealed class NamedPipeWorkerRuntimeCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly string _workerKind;
    private readonly string _endpointName;
    private readonly NamedPipeWorkerRuntimeStateTracker _stateTracker;
    private readonly NamedPipeWorkerRuntimePolicy _runtimePolicy;
    private readonly NamedPipeWorkerRequestExecutor _requestExecutor;
    private readonly NamedPipeWorkerRuntimeLifecycle _runtimeLifecycle;
    private bool _disposed;

    public NamedPipeWorkerRuntimeCoordinator(
        VisionWorkerHostOptions options,
        PythonProcessLauncher processLauncher)
    {
        var hostOptions = options ?? throw new ArgumentNullException(nameof(options));
        _workerKind = hostOptions.WorkerKind;
        _endpointName = hostOptions.EndpointName;
        _stateTracker = new NamedPipeWorkerRuntimeStateTracker(
            hostOptions.WorkerKind,
            hostOptions.EndpointName,
            hostOptions.ProtocolKind);
        _runtimePolicy = new NamedPipeWorkerRuntimePolicy(
            hostOptions.EndpointName,
            _stateTracker);
        _requestExecutor = new NamedPipeWorkerRequestExecutor(
            hostOptions.EndpointName,
            _runtimePolicy);
        var processController = new NamedPipeWorkerProcessController(
            processLauncher ?? throw new ArgumentNullException(nameof(processLauncher)),
            OnProcessExited,
            OnProcessErrorDataReceived);
        _runtimeLifecycle = new NamedPipeWorkerRuntimeLifecycle(
            hostOptions,
            _runtimePolicy,
            processController);
    }

    public string WorkerKind => _workerKind;
    public string EndpointName => _endpointName;

    public string? ActiveRuntimeLabel
    {
        get
        {
            lock (_gate)
            {
                return _stateTracker.ActiveRuntimeLabel;
            }
        }
    }

    public VisionWorkerState State
    {
        get
        {
            lock (_gate)
            {
                return _stateTracker.State;
            }
        }
    }

    public VisionWorkerStatusSnapshot GetStatusSnapshot()
    {
        lock (_gate)
        {
            return _stateTracker.CreateSnapshot(_runtimeLifecycle.ActiveProcessId);
        }
    }

    public void EnsureStarted()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _runtimeLifecycle.EnsureStarted();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _runtimeLifecycle.Stop();
        }
    }

    public VisionWorkerResponse ExecuteRequest(VisionWorkerRequest request, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            ThrowIfDisposed();
            return _requestExecutor.ExecuteRequest(_runtimeLifecycle.Transport, request, timeout);
        }
    }

    public bool PingWorker(TimeSpan timeout)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return _requestExecutor.PingWorker(_runtimeLifecycle.Transport, timeout);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _runtimeLifecycle.Dispose();
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _runtimeLifecycle.HandleUnexpectedExit();
        }
    }

    private void OnProcessErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data))
        {
            return;
        }

        lock (_gate)
        {
            _stateTracker.AppendStdErr(e.Data);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
