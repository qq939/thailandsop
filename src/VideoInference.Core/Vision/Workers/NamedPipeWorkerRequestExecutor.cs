namespace VideoInferenceDemo;

internal sealed class NamedPipeWorkerRequestExecutor
{
    private readonly string _endpointName;
    private readonly NamedPipeWorkerRuntimePolicy _runtimePolicy;

    public NamedPipeWorkerRequestExecutor(
        string endpointName,
        NamedPipeWorkerRuntimePolicy runtimePolicy)
    {
        _endpointName = string.IsNullOrWhiteSpace(endpointName)
            ? throw new ArgumentException("Endpoint name is required.", nameof(endpointName))
            : endpointName;
        _runtimePolicy = runtimePolicy ?? throw new ArgumentNullException(nameof(runtimePolicy));
    }

    public VisionWorkerResponse ExecuteRequest(
        NamedPipeWorkerTransport? transport,
        VisionWorkerRequest request,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(request);

        var connectedTransport = EnsureConnected(transport);
        var previousState = _runtimePolicy.OnRequestStarted();
        try
        {
            var response = connectedTransport.ExecuteRequest(request, timeout);
            _runtimePolicy.OnRequestCompleted(response);
            return response;
        }
        catch (Exception ex)
        {
            _runtimePolicy.OnRequestFailed(ex);
            throw;
        }
        finally
        {
            _runtimePolicy.OnRequestFinished(previousState);
        }
    }

    public bool PingWorker(NamedPipeWorkerTransport? transport, TimeSpan timeout)
    {
        var connectedTransport = EnsureConnected(transport);

        try
        {
            var isAlive = connectedTransport.Ping(timeout);
            _runtimePolicy.OnPingCompleted(isAlive);
            return isAlive;
        }
        catch
        {
            _runtimePolicy.OnPingFailed();
            return false;
        }
    }

    private NamedPipeWorkerTransport EnsureConnected(NamedPipeWorkerTransport? transport)
    {
        return transport ?? throw new InvalidOperationException(
            $"Worker '{_endpointName}' is not connected.");
    }
}
