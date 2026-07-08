using System.Text;

namespace VideoInferenceDemo;

internal sealed class NamedPipeWorkerDiagnosticsState
{
    private readonly StringBuilder _stderrBuffer = new();
    private readonly VisionWorkerProtocolKind _protocolKind;
    private string? _faultMessage;
    private string? _runtimeLabel;
    private int? _lastExitCode;

    public NamedPipeWorkerDiagnosticsState(VisionWorkerProtocolKind protocolKind)
    {
        _protocolKind = protocolKind;
    }

    public int? LastExitCode => _lastExitCode;
    public string? FaultMessage => _faultMessage;

    public VisionWorkerStatusSnapshot CreateSnapshot(
        string workerKind,
        string endpointName,
        VisionWorkerState state,
        int? processId)
    {
        return new VisionWorkerStatusSnapshot(
            workerKind,
            endpointName,
            state,
            BuildRuntimeLabel(),
            processId,
            _lastExitCode,
            _faultMessage,
            GetLastStdErr());
    }

    public string BuildRuntimeLabel()
    {
        if (!string.IsNullOrWhiteSpace(_faultMessage))
        {
            return $"Worker / Faulted / {_faultMessage}";
        }

        if (!string.IsNullOrWhiteSpace(_runtimeLabel))
        {
            return _runtimeLabel;
        }

        return _protocolKind switch
        {
            VisionWorkerProtocolKind.NamedPipe => "Worker / NamedPipe",
            _ => "Worker"
        };
    }

    public void Reset()
    {
        _stderrBuffer.Clear();
        _runtimeLabel = null;
        _lastExitCode = null;
        _faultMessage = null;
    }

    public void RecordRuntimeLabel(string? runtimeLabel)
    {
        if (string.IsNullOrWhiteSpace(runtimeLabel))
        {
            return;
        }

        _runtimeLabel = runtimeLabel;
        _faultMessage = null;
    }

    public void RecordFault(string message)
    {
        _faultMessage = BuildFaultMessage(message);
    }

    public void RecordRequestFailure(string message)
    {
        _faultMessage = message;
    }

    public void RecordExitCode(int? exitCode)
    {
        _lastExitCode = exitCode;
    }

    public void AppendStdErr(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (_stderrBuffer.Length > 0)
        {
            _stderrBuffer.AppendLine();
        }

        _stderrBuffer.Append(line);
        TrimStdErrBuffer();
    }

    private string BuildFaultMessage(string message)
    {
        var stderr = GetLastStdErr();
        return string.IsNullOrWhiteSpace(stderr)
            ? message
            : $"{message} STDERR: {stderr}";
    }

    private string? GetLastStdErr()
    {
        return _stderrBuffer.Length == 0 ? null : _stderrBuffer.ToString();
    }

    private void TrimStdErrBuffer()
    {
        const int maxChars = 4000;
        if (_stderrBuffer.Length <= maxChars)
        {
            return;
        }

        _stderrBuffer.Remove(0, _stderrBuffer.Length - maxChars);
    }
}
