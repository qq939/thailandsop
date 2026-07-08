using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace VideoInferenceDemo.Tests.Hardware;

public sealed class ModbusTcpServerTests
{
    [Fact]
    public void DbConfig_DefaultsModbusTcpServerToAllInterfaces()
    {
        var config = (JsonSerializer.Deserialize<DbConfig>("{}") ?? new DbConfig()).Normalize();

        Assert.False(config.ModbusTcpServer.Enabled);
        Assert.Equal("0.0.0.0", config.ModbusTcpServer.BindAddress);
        Assert.Equal(502, config.ModbusTcpServer.Port);
        Assert.Equal(1, config.ModbusTcpServer.UnitId);
    }

    [Fact]
    public void HoldingRegisterBank_ReadsWritesAndSnapshotsValues()
    {
        var bank = new ModbusHoldingRegisterBank();
        var changes = new List<ModbusHoldingRegisterChangedEventArgs>();
        bank.RegisterChanged += (_, e) => changes.Add(e);

        bank.WriteHoldingRegister(5, 123);
        bank.WriteHoldingRegisters(6, new ushort[] { 456, 789 });
        var values = bank.ReadHoldingRegisters(5, 3);
        var snapshot = bank.Snapshot(5, 3);

        Assert.Equal(new ushort[] { 123, 456, 789 }, values);
        Assert.Equal(new ushort[] { 5, 6, 7 }, snapshot.Select(item => item.Address).ToArray());
        Assert.Equal(new ushort[] { 123, 456, 789 }, snapshot.Select(item => item.Value).ToArray());
        Assert.Equal(3, changes.Count);
    }

    [Fact]
    public void HoldingRegisterBank_RejectsRangesPastAddressSpace()
    {
        var bank = new ModbusHoldingRegisterBank();

        Assert.Throws<ArgumentOutOfRangeException>(() => bank.ReadHoldingRegisters(ushort.MaxValue, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => bank.WriteHoldingRegisters(ushort.MaxValue, new ushort[] { 1, 2 }));
    }

    [Fact]
    public async Task Host_AllowsTcpClientsToReadAndWriteHoldingRegisters()
    {
        var port = GetFreeTcpPort();
        var bank = new ModbusHoldingRegisterBank();
        using var host = new ModbusTcpServerHost(bank);

        var status = host.Start(new ModbusTcpServerOptions
        {
            Enabled = true,
            BindAddress = "127.0.0.1",
            Port = port
        });

        Assert.True(status.IsRunning, status.Message);
        using var client = await WaitForClientAsync(port);
        await client.WriteSingleRegisterAsync(1, 10, 321, CancellationToken.None);
        var values = await client.ReadHoldingRegistersAsync(1, 10, 1, CancellationToken.None);

        Assert.Equal(321, bank.ReadHoldingRegister(10));
        Assert.Equal(new ushort[] { 321 }, values);
    }

    [Fact]
    public async Task Host_AllowsMultipleTcpClientsToUseSeparateRegisterRanges()
    {
        var port = GetFreeTcpPort();
        var bank = new ModbusHoldingRegisterBank();
        using var host = new ModbusTcpServerHost(bank);

        var status = host.Start(new ModbusTcpServerOptions
        {
            Enabled = true,
            BindAddress = "127.0.0.1",
            Port = port
        });

        Assert.True(status.IsRunning, status.Message);
        using var sop2Client = await WaitForClientAsync(port);
        using var sop1Client = await WaitForClientAsync(port);

        await sop2Client.WriteMultipleRegistersAsync(1, 0, new ushort[] { 1, 1, 2 }, CancellationToken.None);
        await sop1Client.WriteMultipleRegistersAsync(1, 10, new ushort[] { 1, 1, 1 }, CancellationToken.None);

        var sop2Values = await sop2Client.ReadHoldingRegistersAsync(1, 0, 3, CancellationToken.None);
        var sop1Values = await sop1Client.ReadHoldingRegistersAsync(1, 10, 3, CancellationToken.None);

        Assert.Equal(new ushort[] { 1, 1, 2 }, sop2Values);
        Assert.Equal(new ushort[] { 1, 1, 1 }, sop1Values);
        Assert.Equal((ushort)0, bank.ReadHoldingRegister(3));
        Assert.Equal((ushort)0, bank.ReadHoldingRegister(9));
    }

    [Fact]
    public async Task Host_ReportsErrorWhenPortIsInUse()
    {
        var port = GetFreeTcpPort();
        using var blocker = new TcpListener(IPAddress.Loopback, port);
        blocker.Start();

        var bank = new ModbusHoldingRegisterBank();
        using var host = new ModbusTcpServerHost(bank);
        host.Start(new ModbusTcpServerOptions
        {
            Enabled = true,
            BindAddress = "127.0.0.1",
            Port = port
        });

        var status = await WaitForStatusAsync(host, item => item.Enabled && !item.IsRunning);

        Assert.False(status.IsRunning);
        Assert.NotEmpty(status.Message);
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<NModbusRegisterClient> WaitForClientAsync(int port)
    {
        Exception? last = null;
        for (var i = 0; i < 20; i++)
        {
            try
            {
                return NModbusRegisterClient.CreateTcp("127.0.0.1", port);
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(50);
            }
        }

        throw new InvalidOperationException("Unable to connect to Modbus TCP test server.", last);
    }

    private static async Task<ModbusTcpServerRuntimeStatus> WaitForStatusAsync(
        ModbusTcpServerHost host,
        Func<ModbusTcpServerRuntimeStatus, bool> predicate)
    {
        for (var i = 0; i < 20; i++)
        {
            var status = host.Status;
            if (predicate(status))
            {
                return status;
            }

            await Task.Delay(50);
        }

        return host.Status;
    }
}
