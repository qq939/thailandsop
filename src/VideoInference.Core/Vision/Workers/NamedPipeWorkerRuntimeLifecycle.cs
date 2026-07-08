namespace VideoInferenceDemo;

internal sealed class NamedPipeWorkerRuntimeLifecycle : IDisposable
{
    private readonly VisionWorkerHostOptions _options;
    private readonly NamedPipeWorkerRuntimePolicy _runtimePolicy;
    private readonly NamedPipeWorkerProcessController _processController;
    private NamedPipeWorkerTransport? _transport;

    public NamedPipeWorkerRuntimeLifecycle(
        VisionWorkerHostOptions options,
        NamedPipeWorkerRuntimePolicy runtimePolicy,
        NamedPipeWorkerProcessController processController)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _runtimePolicy = runtimePolicy ?? throw new ArgumentNullException(nameof(runtimePolicy));
        _processController = processController ?? throw new ArgumentNullException(nameof(processController));
    }

    public int? ActiveProcessId => _processController.ActiveProcessId;
    public NamedPipeWorkerTransport? Transport => _transport;

    public void EnsureStarted()
    {
        if (_processController.IsRunning)
        {
            _runtimePolicy.OnExistingProcessObserved();
            return;
        }

        try
        {
            StopTransportCore();
            _transport = new NamedPipeWorkerTransport(_options.EndpointName);
            _runtimePolicy.OnStartInitiated();
            _processController.Start(_options.ProcessStart);
            _transport.StartServerAndWaitForConnection(_options.ConnectTimeout);
            _runtimePolicy.OnStartCompleted(_transport.SendHello(_options));
        }
        catch (Exception ex)
        {
            _runtimePolicy.OnStartFailed(ex);
            StopTransportCore();
            _processController.Stop();
            throw;
        }
    }

    public void Stop()
    {
        StopTransportCore();
        _processController.Stop();
        _runtimePolicy.OnStopped();
    }

    public void HandleUnexpectedExit()
    {
        _runtimePolicy.OnUnexpectedExit(_processController.TryGetExitCode());
        StopTransportCore();
        _processController.Stop();
    }

    public void Dispose()
    {
        Stop();
    }

    private void StopTransportCore()
    {
        try
        {
            _transport?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _transport = null;
        }
    }
}
