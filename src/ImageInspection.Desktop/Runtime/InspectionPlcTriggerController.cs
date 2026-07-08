using System.Threading;

namespace VideoInferenceDemo.ImageInspection.Runtime;

public sealed class InspectionPlcTriggerOptions
{
    public bool Enabled { get; set; }

    public string Name { get; set; } = "Inspection PLC";

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 502;

    public byte SlaveAddress { get; set; } = 1;

    public int PollIntervalMs { get; set; } = 100;

    public string StationId { get; set; } = string.Empty;

    public string TaskId { get; set; } = string.Empty;

    public ushort TriggerRegisterAddress { get; set; }

    public ushort DoneRegisterAddress { get; set; } = 1;

    public ushort ResultRegisterAddress { get; set; } = 2;

    public int ReadTimeoutMs { get; set; } = 1000;

    public int WriteTimeoutMs { get; set; } = 1000;

    public InspectionPlcTriggerOptions Normalize()
    {
        return new InspectionPlcTriggerOptions
        {
            Enabled = Enabled,
            Name = string.IsNullOrWhiteSpace(Name) ? "Inspection PLC" : Name.Trim(),
            Host = string.IsNullOrWhiteSpace(Host) ? "127.0.0.1" : Host.Trim(),
            Port = Port > 0 ? Port : 502,
            SlaveAddress = SlaveAddress == 0 ? (byte)1 : SlaveAddress,
            PollIntervalMs = Math.Clamp(PollIntervalMs, 50, 2000),
            StationId = StationId?.Trim() ?? string.Empty,
            TaskId = TaskId?.Trim() ?? string.Empty,
            TriggerRegisterAddress = TriggerRegisterAddress,
            DoneRegisterAddress = DoneRegisterAddress,
            ResultRegisterAddress = ResultRegisterAddress,
            ReadTimeoutMs = Math.Clamp(ReadTimeoutMs, 100, 10000),
            WriteTimeoutMs = Math.Clamp(WriteTimeoutMs, 100, 10000)
        };
    }
}

public sealed record InspectionPlcTriggerStatus(
    InspectionTriggerStatusKind Kind,
    string Message,
    string StationId,
    string TaskId,
    string? TaskInstanceId,
    InspectionCycleDecision Decision,
    DateTimeOffset Timestamp);

public sealed class InspectionPlcTriggerController : IDisposable
{
    // First version: edit this DTO directly, then rebuild/restart the desktop app.
    public static InspectionPlcTriggerOptions DefaultOptions { get; } = new()
    {
        Enabled = false,
        Host = "127.0.0.1",
        Port = 502,
        SlaveAddress = 1,
        PollIntervalMs = 100,
        StationId = string.Empty,
        TaskId = string.Empty,
        TriggerRegisterAddress = 0,
        DoneRegisterAddress = 1,
        ResultRegisterAddress = 2
    };

    private readonly Func<string, string, InspectionTaskSessionViewModel?> _resolveTask;
    private readonly Func<InspectionTaskSessionViewModel, Task<InspectionRuntimeTaskResult>> _triggerTaskAsync;
    private readonly Func<InspectionPlcTriggerOptions, IModbusRegisterClient> _clientFactory;
    private readonly TimeSpan _reconnectDelay;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private IModbusRegisterClient? _client;
    private InspectionPlcTriggerOptions _options = new();
    private bool _triggerLatched;

    public event EventHandler<InspectionPlcTriggerStatus>? StatusChanged;

    public InspectionPlcTriggerStatus CurrentStatus { get; private set; } = new(
        InspectionTriggerStatusKind.Disabled,
        "PLC\u89e6\u53d1\u672a\u542f\u7528",
        string.Empty,
        string.Empty,
        null,
        InspectionCycleDecision.Unknown,
        DateTimeOffset.Now);

    public InspectionPlcTriggerController(
        Func<string, string, InspectionTaskSessionViewModel?> resolveTask,
        Func<InspectionTaskSessionViewModel, Task<InspectionRuntimeTaskResult>> triggerTaskAsync)
        : this(
            resolveTask,
            triggerTaskAsync,
            options => NModbusRegisterClient.CreateTcp(
                options.Host,
                options.Port,
                options.ReadTimeoutMs,
                options.WriteTimeoutMs))
    {
    }

    internal InspectionPlcTriggerController(
        Func<string, string, InspectionTaskSessionViewModel?> resolveTask,
        Func<InspectionTaskSessionViewModel, Task<InspectionRuntimeTaskResult>> triggerTaskAsync,
        Func<InspectionPlcTriggerOptions, IModbusRegisterClient> clientFactory,
        TimeSpan? reconnectDelay = null)
    {
        _resolveTask = resolveTask ?? throw new ArgumentNullException(nameof(resolveTask));
        _triggerTaskAsync = triggerTaskAsync ?? throw new ArgumentNullException(nameof(triggerTaskAsync));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _reconnectDelay = reconnectDelay.GetValueOrDefault(TimeSpan.FromSeconds(1));
        if (_reconnectDelay <= TimeSpan.Zero)
        {
            _reconnectDelay = TimeSpan.FromSeconds(1);
        }
    }

    public void Start(InspectionPlcTriggerOptions? options = null)
    {
        Stop();

        _options = (options ?? DefaultOptions).Normalize();
        if (!_options.Enabled)
        {
            CameraDiagnostics.Info("inspection-plc", "PLC trigger controller is disabled.");
            PublishStatus(InspectionTriggerStatusKind.Disabled, "PLC\u89e6\u53d1\u672a\u542f\u7528");
            return;
        }

        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunConnectionLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        var cts = _cts;
        _cts = null;
        if (cts != null)
        {
            cts.Cancel();
            try
            {
                _loopTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }

            cts.Dispose();
        }

        _loopTask = null;
        _client?.Dispose();
        _client = null;
        _triggerLatched = false;
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task RunConnectionLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_client == null)
                {
                    _client = _clientFactory(_options);
                    _triggerLatched = false;
                    CameraDiagnostics.Info(
                        "inspection-plc",
                        $"Started PLC trigger controller '{_options.Name}' at {_options.Host}:{_options.Port}.");
                    PublishStatus(InspectionTriggerStatusKind.Waiting, "\u7b49\u5f85\u89e6\u53d1\u4fe1\u53f7");
                }

                await RunPollingLoopAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                DisposeClient();
                _triggerLatched = false;
                CameraDiagnostics.Error(
                    "inspection-plc",
                    $"PLC trigger controller '{_options.Name}' connection failed at {_options.Host}:{_options.Port}: {ex.Message}");
                PublishStatus(InspectionTriggerStatusKind.Error, $"PLC\u8fde\u63a5\u5931\u8d25: {ex.Message}");
                await DelayReconnectAsync(ct).ConfigureAwait(false);
            }
        }
    }

    private async Task RunPollingLoopAsync(CancellationToken ct)
    {
        if (_client == null)
        {
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            var triggerValue = await ReadRegisterAsync(_options.TriggerRegisterAddress, ct).ConfigureAwait(false);
            if (triggerValue == 0)
            {
                if (_triggerLatched)
                {
                    await WriteDoneAndResultAsync(0, PlcResultCodes.Unknown, ct).ConfigureAwait(false);
                    _triggerLatched = false;
                    PublishStatus(InspectionTriggerStatusKind.Waiting, "\u7b49\u5f85\u89e6\u53d1\u4fe1\u53f7");
                }

                await DelayAsync(ct).ConfigureAwait(false);
                continue;
            }

            if (!_triggerLatched)
            {
                _triggerLatched = true;
                PublishStatus(InspectionTriggerStatusKind.Received, "\u6536\u5230\u89e6\u53d1\u4fe1\u53f7");
                await ExecuteTriggeredTaskAsync(ct).ConfigureAwait(false);
            }

            await DelayAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task ExecuteTriggeredTaskAsync(CancellationToken ct)
    {
        var task = _resolveTask(_options.StationId, _options.TaskId);
        if (task == null)
        {
            CameraDiagnostics.Error(
                "inspection-plc",
                $"No running task matched StationId='{_options.StationId}', TaskId='{_options.TaskId}'.");
            await WriteDoneAndResultAsync(1, PlcResultCodes.Error, ct).ConfigureAwait(false);
            PublishStatus(InspectionTriggerStatusKind.Error, "\u672a\u5339\u914d\u4efb\u52a1");
            return;
        }

        PublishStatus(InspectionTriggerStatusKind.Processing, "\u5904\u7406\u4e2d", task);
        InspectionRuntimeTaskResult result;
        try
        {
            result = await _triggerTaskAsync(task).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CameraDiagnostics.Error("inspection-plc", $"PLC triggered task failed: {ex.Message}");
            await WriteDoneAndResultAsync(1, PlcResultCodes.Error, ct).ConfigureAwait(false);
            PublishStatus(InspectionTriggerStatusKind.Error, $"\u89e6\u53d1\u5f02\u5e38: {ex.Message}", task);
            return;
        }

        var code = result.Executed
            ? MapDecision(result.Decision)
            : PlcResultCodes.Error;
        await WriteDoneAndResultAsync(1, code, ct).ConfigureAwait(false);
        PublishStatus(
            result.Executed ? MapCompletedStatusKind(result.Decision) : InspectionTriggerStatusKind.Error,
            result.Executed ? $"\u5df2\u5b8c\u6210 {FormatDecision(result.Decision)}" : "\u89e6\u53d1\u5f02\u5e38",
            task,
            result.Decision);
    }

    private async Task<ushort> ReadRegisterAsync(ushort address, CancellationToken ct)
    {
        var values = await _client!.ReadHoldingRegistersAsync(_options.SlaveAddress, address, 1, ct).ConfigureAwait(false);
        return values.Length == 0 ? (ushort)0 : values[0];
    }

    private Task WriteDoneAndResultAsync(ushort done, ushort result, CancellationToken ct)
    {
        if (_options.ResultRegisterAddress == _options.DoneRegisterAddress + 1)
        {
            return _client!.WriteMultipleRegistersAsync(
                _options.SlaveAddress,
                _options.DoneRegisterAddress,
                [done, result],
                ct);
        }

        return WriteSeparateDoneAndResultAsync(done, result, ct);
    }

    private async Task WriteSeparateDoneAndResultAsync(ushort done, ushort result, CancellationToken ct)
    {
        await _client!.WriteSingleRegisterAsync(_options.SlaveAddress, _options.DoneRegisterAddress, done, ct)
            .ConfigureAwait(false);
        await _client.WriteSingleRegisterAsync(_options.SlaveAddress, _options.ResultRegisterAddress, result, ct)
            .ConfigureAwait(false);
    }

    private Task DelayAsync(CancellationToken ct)
    {
        return Task.Delay(_options.PollIntervalMs, ct);
    }

    private Task DelayReconnectAsync(CancellationToken ct)
    {
        return Task.Delay(_reconnectDelay, ct);
    }

    private void DisposeClient()
    {
        try
        {
            _client?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _client = null;
        }
    }

    private static ushort MapDecision(InspectionCycleDecision decision)
    {
        return decision switch
        {
            InspectionCycleDecision.Ok => PlcResultCodes.Ok,
            InspectionCycleDecision.Ng => PlcResultCodes.Ng,
            InspectionCycleDecision.Warning => PlcResultCodes.Warning,
            _ => PlcResultCodes.Unknown
        };
    }

    private void PublishStatus(
        InspectionTriggerStatusKind kind,
        string message,
        InspectionTaskSessionViewModel? task = null,
        InspectionCycleDecision decision = InspectionCycleDecision.Unknown)
    {
        var status = new InspectionPlcTriggerStatus(
            kind,
            message,
            _options.StationId,
            _options.TaskId,
            task?.Id,
            decision,
            DateTimeOffset.Now);
        CurrentStatus = status;
        StatusChanged?.Invoke(this, status);
    }

    private static InspectionTriggerStatusKind MapCompletedStatusKind(InspectionCycleDecision decision)
    {
        return decision switch
        {
            InspectionCycleDecision.Ok => InspectionTriggerStatusKind.CompletedOk,
            InspectionCycleDecision.Ng => InspectionTriggerStatusKind.CompletedNg,
            InspectionCycleDecision.Warning => InspectionTriggerStatusKind.CompletedWarning,
            _ => InspectionTriggerStatusKind.Error
        };
    }

    private static string FormatDecision(InspectionCycleDecision decision)
    {
        return decision switch
        {
            InspectionCycleDecision.Ok => "OK",
            InspectionCycleDecision.Ng => "NG",
            InspectionCycleDecision.Warning => "WARNING",
            _ => "UNKNOWN"
        };
    }

    private static class PlcResultCodes
    {
        public const ushort Unknown = 0;
        public const ushort Ok = 1;
        public const ushort Ng = 2;
        public const ushort Warning = 3;
        public const ushort Error = 4;
    }
}
