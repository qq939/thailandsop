using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace VideoInferenceDemo;

internal sealed class NamedPipeWorkerTransport : IDisposable
{
    private readonly string _endpointName;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private NamedPipeServerStream? _pipeServer;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public NamedPipeWorkerTransport(string endpointName)
    {
        _endpointName = string.IsNullOrWhiteSpace(endpointName)
            ? throw new ArgumentException("Endpoint name is required.", nameof(endpointName))
            : endpointName;
    }

    public void StartServerAndWaitForConnection(TimeSpan timeout)
    {
        Stop();
        _pipeServer = new NamedPipeServerStream(
            _endpointName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        using var cancellationTokenSource = new CancellationTokenSource(timeout);
        _pipeServer.WaitForConnectionAsync(cancellationTokenSource.Token).GetAwaiter().GetResult();
        _reader = new StreamReader(_pipeServer, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        _writer = new StreamWriter(_pipeServer, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true
        };
    }

    public string? SendHello(VisionWorkerHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        EnsureConnected();
        WriteMessage(new WorkerHelloRequest
        {
            Type = "hello",
            ProtocolVersion = 1,
            WorkerKind = options.WorkerKind,
            TaskId = options.TaskId,
            TaskKind = options.TaskKind.ToString(),
            RuntimeKind = options.RuntimeKind.ToString(),
            Config = options.Metadata
        });

        var response = ReadMessage<WorkerHelloResponse>(options.ConnectTimeout);
        if (!string.Equals(response.Type, "hello_ack", StringComparison.OrdinalIgnoreCase) ||
            !response.Ready)
        {
            throw new InvalidOperationException(
                $"Worker '{_endpointName}' did not acknowledge hello successfully.");
        }

        return response.RuntimeLabel;
    }

    public VisionWorkerResponse ExecuteRequest(VisionWorkerRequest request, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(request);

        EnsureConnected();
        WriteMessage(new WorkerInferenceRequest
        {
            Type = "infer",
            RequestId = request.RequestId,
            FrameId = request.FrameId,
            TimestampMs = ResolveTimestampMs(request),
            TimestampUtc = request.TimestampUtc,
            TaskKind = request.TaskKind.ToString(),
            Frame = new WorkerFrameDto
            {
                Width = request.Frame.Width,
                Height = request.Frame.Height,
                Stride = request.Frame.Stride,
                PixelFormat = request.Frame.PixelFormat,
                ImageBase64 = Convert.ToBase64String(request.Frame.ImageBytes)
            },
            Roi = request.Roi == null
                ? null
                : new WorkerRegionDto
                {
                    X = request.Roi.X,
                    Y = request.Roi.Y,
                    Width = request.Roi.Width,
                    Height = request.Roi.Height
                },
            Metadata = request.Metadata
        });

        var response = ReadMessage<WorkerInferenceResponse>(timeout);
        var metadata = response.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new VisionWorkerResponse
        {
            RequestId = response.RequestId ?? request.RequestId,
            FrameId = response.FrameId ?? request.FrameId,
            IsSuccess = response.IsSuccess,
            RuntimeLabel = response.RuntimeLabel,
            PayloadJson = response.Payload.HasValue ? response.Payload.Value.GetRawText() : null,
            ErrorCode = response.ErrorCode,
            ErrorMessage = response.Message,
            Metadata = metadata
        };
    }

    public bool Ping(TimeSpan timeout)
    {
        EnsureConnected();
        WriteMessage(new WorkerPingRequest { Type = "ping" });
        var response = ReadMessage<WorkerPingResponse>(timeout);
        return string.Equals(response.Type, "pong", StringComparison.OrdinalIgnoreCase);
    }

    public void Stop()
    {
        try
        {
            _writer?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _writer = null;
        }

        try
        {
            _reader?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _reader = null;
        }

        try
        {
            _pipeServer?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _pipeServer = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private void EnsureConnected()
    {
        if (_pipeServer == null || _reader == null || _writer == null || !_pipeServer.IsConnected)
        {
            throw new InvalidOperationException(
                $"Worker '{_endpointName}' is not connected.");
        }
    }

    private void WriteMessage<T>(T message)
    {
        if (_writer == null)
        {
            throw new InvalidOperationException($"Worker '{_endpointName}' writer is not available.");
        }

        var json = JsonSerializer.Serialize(message, _jsonOptions);
        _writer.WriteLine(json);
    }

    private T ReadMessage<T>(TimeSpan timeout)
    {
        if (_reader == null)
        {
            throw new InvalidOperationException($"Worker '{_endpointName}' reader is not available.");
        }

        var readTask = _reader.ReadLineAsync();
        if (!readTask.Wait(timeout))
        {
            throw new TimeoutException(
                $"Timed out waiting for worker '{_endpointName}' response.");
        }

        var line = readTask.Result;
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new InvalidOperationException(
                $"Worker '{_endpointName}' returned an empty response.");
        }

        return JsonSerializer.Deserialize<T>(line, _jsonOptions)
               ?? throw new InvalidOperationException(
                   $"Worker '{_endpointName}' returned an invalid response.");
    }

    private static long ResolveTimestampMs(VisionWorkerRequest request)
    {
        if (request.Metadata.TryGetValue("timestampMs", out var rawValue) &&
            long.TryParse(rawValue, out var parsed) &&
            parsed >= 0)
        {
            return parsed;
        }

        var unixMs = request.TimestampUtc.ToUnixTimeMilliseconds();
        return Math.Max(0, unixMs);
    }
}

internal sealed class WorkerHelloRequest
{
    public required string Type { get; init; }
    public required int ProtocolVersion { get; init; }
    public required string WorkerKind { get; init; }
    public required string TaskId { get; init; }
    public required string TaskKind { get; init; }
    public required string RuntimeKind { get; init; }
    public required IReadOnlyDictionary<string, string> Config { get; init; }
}

internal sealed class WorkerHelloResponse
{
    public string? Type { get; init; }
    public bool Ready { get; init; }
    public string? RuntimeLabel { get; init; }
}

internal sealed class WorkerPingRequest
{
    public required string Type { get; init; }
}

internal sealed class WorkerPingResponse
{
    public string? Type { get; init; }
}

internal sealed class WorkerInferenceRequest
{
    public required string Type { get; init; }
    public required string RequestId { get; init; }
    public required long FrameId { get; init; }
    public required long TimestampMs { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public required string TaskKind { get; init; }
    public required WorkerFrameDto Frame { get; init; }
    public WorkerRegionDto? Roi { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

internal sealed class WorkerFrameDto
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Stride { get; init; }
    public required string PixelFormat { get; init; }
    public required string ImageBase64 { get; init; }
}

internal sealed class WorkerRegionDto
{
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
}

internal sealed class WorkerInferenceResponse
{
    public string? Type { get; init; }
    public string? RequestId { get; init; }
    public long? FrameId { get; init; }
    public bool IsSuccess { get; init; }
    public string? RuntimeLabel { get; init; }
    public JsonElement? Payload { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
