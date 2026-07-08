using System.Net;
using System.Net.Sockets;
using NModbus;

namespace VideoInferenceDemo;

public sealed record ModbusTcpServerRuntimeStatus(
    bool Enabled,
    bool IsRunning,
    string Endpoint,
    string Message)
{
    public static ModbusTcpServerRuntimeStatus Disabled { get; } =
        new(false, false, string.Empty, "Disabled");
}

public sealed class ModbusTcpServerHost : IDisposable
{
    private readonly ModbusHoldingRegisterBank _holdingRegisters;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private TcpListener? _listener;
    private IModbusSlaveNetwork? _network;
    private Task? _listenTask;
    private ModbusTcpServerRuntimeStatus _status = ModbusTcpServerRuntimeStatus.Disabled;
    private bool _disposed;

    public ModbusTcpServerHost(ModbusHoldingRegisterBank holdingRegisters)
    {
        _holdingRegisters = holdingRegisters ?? throw new ArgumentNullException(nameof(holdingRegisters));
    }

    public event EventHandler<ModbusTcpServerRuntimeStatus>? StatusChanged;

    public ModbusTcpServerRuntimeStatus Status
    {
        get
        {
            lock (_lock)
            {
                return _status;
            }
        }
    }

    public ModbusTcpServerRuntimeStatus Restart(ModbusTcpServerOptions? options)
    {
        Stop();
        return Start(options);
    }

    public ModbusTcpServerRuntimeStatus Start(ModbusTcpServerOptions? options)
    {
        ThrowIfDisposed();
        var normalized = (options ?? new ModbusTcpServerOptions()).Normalize();
        if (!normalized.Enabled)
        {
            SetStatus(ModbusTcpServerRuntimeStatus.Disabled);
            return Status;
        }

        try
        {
            var endpoint = BuildEndpoint(normalized);
            var listener = new TcpListener(endpoint);
            var factory = new ModbusFactory();
            var dataStore = new ModbusServerDataStore(_holdingRegisters);
            var network = factory.CreateSlaveNetwork(listener);
            network.AddSlave(factory.CreateSlave(normalized.UnitId, dataStore));

            var cts = new CancellationTokenSource();
            var listenTask = Task.Run(() => network.ListenAsync(cts.Token), cts.Token);
            listenTask.ContinueWith(
                task => OnListenTaskCompleted(task, normalized),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            lock (_lock)
            {
                _cts = cts;
                _listener = listener;
                _network = network;
                _listenTask = listenTask;
            }

            var status = new ModbusTcpServerRuntimeStatus(
                true,
                true,
                $"{normalized.BindAddress}:{normalized.Port}",
                "Running");
            SetStatus(status);
            CameraDiagnostics.Info("modbus-server", $"Modbus TCP server listening on {status.Endpoint}, unit={normalized.UnitId}.");
            return status;
        }
        catch (Exception ex)
        {
            CleanupTransport();
            var status = new ModbusTcpServerRuntimeStatus(
                true,
                false,
                $"{normalized.BindAddress}:{normalized.Port}",
                ex.Message);
            SetStatus(status);
            CameraDiagnostics.Error("modbus-server", $"Failed to start Modbus TCP server {status.Endpoint}: {ex.Message}");
            return status;
        }
    }

    public void Stop()
    {
        CleanupTransport();
        SetStatus(ModbusTcpServerRuntimeStatus.Disabled);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CleanupTransport();
    }

    private void OnListenTaskCompleted(Task task, ModbusTcpServerOptions options)
    {
        if (task.IsCanceled)
        {
            return;
        }

        var exception = task.Exception?.GetBaseException();
        if (exception == null)
        {
            return;
        }

        var status = new ModbusTcpServerRuntimeStatus(
            true,
            false,
            $"{options.BindAddress}:{options.Port}",
            exception.Message);
        SetStatus(status);
        CameraDiagnostics.Error("modbus-server", $"Modbus TCP server stopped with error: {exception.Message}");
    }

    private static IPEndPoint BuildEndpoint(ModbusTcpServerOptions options)
    {
        if (!IPAddress.TryParse(options.BindAddress, out var address))
        {
            throw new InvalidOperationException($"Invalid Modbus TCP bind address: {options.BindAddress}");
        }

        return new IPEndPoint(address, options.Port);
    }

    private void CleanupTransport()
    {
        CancellationTokenSource? cts;
        TcpListener? listener;
        IModbusSlaveNetwork? network;

        lock (_lock)
        {
            cts = _cts;
            listener = _listener;
            network = _network;
            _cts = null;
            _listener = null;
            _network = null;
            _listenTask = null;
        }

        try
        {
            cts?.Cancel();
        }
        catch
        {
        }

        try
        {
            listener?.Stop();
        }
        catch
        {
        }

        try
        {
            (network as IDisposable)?.Dispose();
        }
        catch
        {
        }

        cts?.Dispose();
    }

    private void SetStatus(ModbusTcpServerRuntimeStatus status)
    {
        lock (_lock)
        {
            if (_status.Equals(status))
            {
                return;
            }

            _status = status;
        }

        StatusChanged?.Invoke(this, status);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ModbusTcpServerHost));
        }
    }

    private sealed class ModbusServerDataStore : ISlaveDataStore
    {
        public ModbusServerDataStore(ModbusHoldingRegisterBank holdingRegisters)
        {
            HoldingRegisters = new HoldingRegisterPointSource(holdingRegisters);
            InputRegisters = new MemoryPointSource<ushort>();
            CoilDiscretes = new MemoryPointSource<bool>();
            CoilInputs = new MemoryPointSource<bool>();
        }

        public IPointSource<bool> CoilDiscretes { get; }
        public IPointSource<bool> CoilInputs { get; }
        public IPointSource<ushort> HoldingRegisters { get; }
        public IPointSource<ushort> InputRegisters { get; }
    }

    private sealed class HoldingRegisterPointSource : IPointSource<ushort>
    {
        private readonly ModbusHoldingRegisterBank _bank;

        public HoldingRegisterPointSource(ModbusHoldingRegisterBank bank)
        {
            _bank = bank ?? throw new ArgumentNullException(nameof(bank));
        }

        public ushort[] ReadPoints(ushort startAddress, ushort numberOfPoints)
        {
            return _bank.ReadHoldingRegisters(startAddress, numberOfPoints);
        }

        public void WritePoints(ushort startAddress, ushort[] points)
        {
            _bank.WriteHoldingRegisters(startAddress, points ?? Array.Empty<ushort>());
        }
    }

    private sealed class MemoryPointSource<T> : IPointSource<T>
    {
        private const int PointCount = ushort.MaxValue + 1;
        private readonly T[] _points = new T[PointCount];
        private readonly object _lock = new();

        public T[] ReadPoints(ushort startAddress, ushort numberOfPoints)
        {
            ValidateRange(startAddress, numberOfPoints);
            var values = new T[numberOfPoints];
            if (numberOfPoints == 0)
            {
                return values;
            }

            lock (_lock)
            {
                Array.Copy(_points, startAddress, values, 0, numberOfPoints);
            }

            return values;
        }

        public void WritePoints(ushort startAddress, T[] points)
        {
            ArgumentNullException.ThrowIfNull(points);
            if (points.Length > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(points), "Point count exceeds Modbus address space.");
            }

            ValidateRange(startAddress, (ushort)points.Length);
            if (points.Length == 0)
            {
                return;
            }

            lock (_lock)
            {
                Array.Copy(points, 0, _points, startAddress, points.Length);
            }
        }

        private static void ValidateRange(ushort startAddress, ushort numberOfPoints)
        {
            if ((uint)startAddress + numberOfPoints > PointCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(numberOfPoints),
                    numberOfPoints,
                    "Modbus point range exceeds address 65535.");
            }
        }
    }
}
