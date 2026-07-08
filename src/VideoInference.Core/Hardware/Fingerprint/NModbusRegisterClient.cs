using System.IO.Ports;
using System.Net.Sockets;
using NModbus;
using NModbus.IO;

namespace VideoInferenceDemo;

public sealed class NModbusRegisterClient : IModbusRegisterClient
{
    private readonly IModbusMaster _master;
    private readonly IDisposable? _transportOwner;

    private NModbusRegisterClient(IModbusMaster master, IDisposable? transportOwner)
    {
        _master = master ?? throw new ArgumentNullException(nameof(master));
        _transportOwner = transportOwner;
    }

    public static NModbusRegisterClient Create(FingerprintModuleOptions options)
    {
        var normalized = (options ?? new FingerprintModuleOptions()).Normalize();
        return normalized.ConnectionKind == FingerprintConnectionKind.Tcp
            ? CreateTcp(normalized)
            : CreateSerialRtu(normalized);
    }

    public static NModbusRegisterClient CreateSerialRtu(FingerprintModuleOptions options)
    {
        var normalized = (options ?? new FingerprintModuleOptions()).Normalize();
        var serialPort = new SerialPort(
            normalized.PortName,
            normalized.BaudRate,
            ParseParity(normalized.Parity),
            normalized.DataBits,
            normalized.StopBits == 2 ? StopBits.Two : StopBits.One)
        {
            ReadTimeout = normalized.ReadTimeoutMs,
            WriteTimeout = normalized.WriteTimeoutMs
        };
        serialPort.Open();

        var resource = new SerialPortStreamResource(serialPort);
        var factory = new ModbusFactory();
        var master = factory.CreateRtuMaster(resource);
        ConfigureTransport(master, normalized);
        return new NModbusRegisterClient(master, resource);
    }

    public static NModbusRegisterClient CreateTcp(FingerprintModuleOptions options)
    {
        var normalized = (options ?? new FingerprintModuleOptions()).Normalize();
        return CreateTcp(normalized.Host, normalized.TcpPort, normalized.ReadTimeoutMs, normalized.WriteTimeoutMs);
    }

    public static NModbusRegisterClient CreateTcp(string host, int port, int readTimeoutMs = 1000, int writeTimeoutMs = 1000)
    {
        var tcpClient = new TcpClient();
        tcpClient.ReceiveTimeout = readTimeoutMs;
        tcpClient.SendTimeout = writeTimeoutMs;
        tcpClient.Connect(host, port);

        var factory = new ModbusFactory();
        var master = factory.CreateMaster(tcpClient);
        ConfigureTransport(master, readTimeoutMs, writeTimeoutMs);
        return new NModbusRegisterClient(master, tcpClient);
    }

    public Task<ushort[]> ReadHoldingRegistersAsync(byte slaveAddress, ushort startAddress, ushort numberOfPoints, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return _master.ReadHoldingRegistersAsync(slaveAddress, startAddress, numberOfPoints);
    }

    public Task WriteSingleRegisterAsync(byte slaveAddress, ushort registerAddress, ushort value, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return _master.WriteSingleRegisterAsync(slaveAddress, registerAddress, value);
    }

    public Task WriteMultipleRegistersAsync(byte slaveAddress, ushort startAddress, ushort[] values, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return _master.WriteMultipleRegistersAsync(slaveAddress, startAddress, values);
    }

    public Task WriteSingleCoilAsync(byte slaveAddress, ushort coilAddress, bool value, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return _master.WriteSingleCoilAsync(slaveAddress, coilAddress, value);
    }

    public Task WriteMultipleCoilsAsync(byte slaveAddress, ushort startAddress, bool[] values, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return _master.WriteMultipleCoilsAsync(slaveAddress, startAddress, values);
    }

    public void Dispose()
    {
        _master.Dispose();
        _transportOwner?.Dispose();
    }

    private static void ConfigureTransport(IModbusMaster master, FingerprintModuleOptions options)
    {
        ConfigureTransport(master, options.ReadTimeoutMs, options.WriteTimeoutMs);
    }

    private static void ConfigureTransport(IModbusMaster master, int readTimeoutMs, int writeTimeoutMs)
    {
        master.Transport.ReadTimeout = readTimeoutMs;
        master.Transport.WriteTimeout = writeTimeoutMs;
        master.Transport.Retries = 0;
        master.Transport.WaitToRetryMilliseconds = 0;
    }

    private static Parity ParseParity(string value)
    {
        return Enum.TryParse<Parity>(value, ignoreCase: true, out var parity)
            ? parity
            : Parity.None;
    }

    private sealed class SerialPortStreamResource : IStreamResource
    {
        private readonly SerialPort _serialPort;
        private bool _disposed;

        public SerialPortStreamResource(SerialPort serialPort)
        {
            _serialPort = serialPort ?? throw new ArgumentNullException(nameof(serialPort));
        }

        public int InfiniteTimeout => SerialPort.InfiniteTimeout;

        public int ReadTimeout
        {
            get => _serialPort.ReadTimeout;
            set => _serialPort.ReadTimeout = value;
        }

        public int WriteTimeout
        {
            get => _serialPort.WriteTimeout;
            set => _serialPort.WriteTimeout = value;
        }

        public void DiscardInBuffer()
        {
            _serialPort.DiscardInBuffer();
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            return _serialPort.Read(buffer, offset, count);
        }

        public void Write(byte[] buffer, int offset, int count)
        {
            _serialPort.Write(buffer, offset, count);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _serialPort.Dispose();
        }
    }
}
