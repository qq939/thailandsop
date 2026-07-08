using VideoInferenceDemo.ImageInspection;
using VideoInferenceDemo.ImageInspection.Runtime;

namespace VideoInferenceDemo.Tests.Inspection;

public sealed class InspectionPlcTriggerControllerTests
{
    [Fact]
    public async Task PlcTrigger_LatchesUntilPlcClearsTriggerAndThenClearsDoneAndResult()
    {
        var client = new FakeModbusClient();
        var task = new InspectionTaskSessionViewModel(
            "task-instance-1",
            "Task 1",
            "appearance-check",
            "model-a",
            "p01",
            "station-1",
            InspectionActionTypes.RoiInspection,
            "appearance-check",
            InspectionTaskTriggerMode.TriggerCommand)
        {
            IsRunning = true
        };
        var triggerCount = 0;
        using var controller = new InspectionPlcTriggerController(
            (_, _) => task,
            _ =>
            {
                Interlocked.Increment(ref triggerCount);
                return Task.FromResult(new InspectionRuntimeTaskResult(
                    true,
                    InspectionCycleDecision.Ng,
                    "plc-trigger",
                    DateTimeOffset.Now));
            },
            _ => client);
        var options = new InspectionPlcTriggerOptions
        {
            Enabled = true,
            PollIntervalMs = 50,
            TriggerRegisterAddress = 10,
            DoneRegisterAddress = 11,
            ResultRegisterAddress = 12,
            StationId = "station-1",
            TaskId = "appearance-check"
        };

        client.SetRegister(10, 1);
        controller.Start(options);

        await WaitUntilAsync(() => Volatile.Read(ref triggerCount) == 1);
        await Task.Delay(180);

        Assert.Equal(1, Volatile.Read(ref triggerCount));
        Assert.Equal((ushort)1, client.GetRegister(11));
        Assert.Equal((ushort)2, client.GetRegister(12));

        client.SetRegister(10, 0);
        await WaitUntilAsync(() => client.GetRegister(11) == 0 && client.GetRegister(12) == 0);

        Assert.Equal(1, Volatile.Read(ref triggerCount));
    }

    [Fact]
    public async Task PlcTrigger_PublishesStatusThroughCompletionAndReset()
    {
        var client = new FakeModbusClient();
        var statuses = new List<InspectionPlcTriggerStatus>();
        var statusSync = new object();
        var task = new InspectionTaskSessionViewModel(
            "task-instance-1",
            "Task 1",
            "appearance-check",
            "model-a",
            "p01",
            "station-1",
            InspectionActionTypes.RoiInspection,
            "appearance-check",
            InspectionTaskTriggerMode.TriggerCommand)
        {
            IsRunning = true
        };
        using var controller = new InspectionPlcTriggerController(
            (_, _) => task,
            _ => Task.FromResult(new InspectionRuntimeTaskResult(
                true,
                InspectionCycleDecision.Ok,
                "plc-trigger",
                DateTimeOffset.Now)),
            _ => client);
        controller.StatusChanged += (_, status) =>
        {
            lock (statusSync)
            {
                statuses.Add(status);
            }
        };
        var options = new InspectionPlcTriggerOptions
        {
            Enabled = true,
            PollIntervalMs = 50,
            TriggerRegisterAddress = 10,
            DoneRegisterAddress = 11,
            ResultRegisterAddress = 12,
            StationId = "station-1",
            TaskId = "appearance-check"
        };

        client.SetRegister(10, 1);
        controller.Start(options);

        await WaitUntilAsync(() => HasStatus(statuses, statusSync, InspectionTriggerStatusKind.CompletedOk));

        Assert.Equal((ushort)1, client.GetRegister(11));
        Assert.Equal((ushort)1, client.GetRegister(12));
        Assert.Contains(Snapshot(statuses, statusSync), status => status.Kind == InspectionTriggerStatusKind.Received);
        Assert.Contains(Snapshot(statuses, statusSync), status => status.Kind == InspectionTriggerStatusKind.Processing);
        Assert.Contains(Snapshot(statuses, statusSync), status =>
            status.Kind == InspectionTriggerStatusKind.CompletedOk &&
            status.Message == "\u5df2\u5b8c\u6210 OK" &&
            status.TaskInstanceId == "task-instance-1");

        client.SetRegister(10, 0);
        await WaitUntilAsync(() =>
            client.GetRegister(11) == 0 &&
            client.GetRegister(12) == 0 &&
            Snapshot(statuses, statusSync).Count(status => status.Kind == InspectionTriggerStatusKind.Waiting) >= 2);
    }

    [Fact]
    public async Task PlcTrigger_WritesErrorWhenNoCommandTaskMatches()
    {
        var client = new FakeModbusClient();
        var statuses = new List<InspectionPlcTriggerStatus>();
        var statusSync = new object();
        using var controller = new InspectionPlcTriggerController(
            (_, _) => null,
            _ => throw new InvalidOperationException("No task should be triggered."),
            _ => client);
        controller.StatusChanged += (_, status) =>
        {
            lock (statusSync)
            {
                statuses.Add(status);
            }
        };
        var options = new InspectionPlcTriggerOptions
        {
            Enabled = true,
            PollIntervalMs = 50,
            TriggerRegisterAddress = 20,
            DoneRegisterAddress = 21,
            ResultRegisterAddress = 22
        };

        client.SetRegister(20, 1);
        controller.Start(options);

        await WaitUntilAsync(() =>
            client.GetRegister(21) == 1 &&
            client.GetRegister(22) == 4 &&
            Snapshot(statuses, statusSync).Any(status =>
                status.Kind == InspectionTriggerStatusKind.Error &&
                status.Message == "\u672a\u5339\u914d\u4efb\u52a1"));

        Assert.Contains(Snapshot(statuses, statusSync), status =>
            status.Kind == InspectionTriggerStatusKind.Error &&
            status.Message == "\u672a\u5339\u914d\u4efb\u52a1");
    }

    [Fact]
    public async Task PlcTrigger_StartConnectionFailurePublishesErrorWithoutThrowing()
    {
        var statuses = new List<InspectionPlcTriggerStatus>();
        var statusSync = new object();
        using var controller = new InspectionPlcTriggerController(
            (_, _) => null,
            _ => throw new InvalidOperationException("No task should be triggered."),
            _ => throw new InvalidOperationException("connect refused"),
            TimeSpan.FromMilliseconds(20));
        controller.StatusChanged += (_, status) =>
        {
            lock (statusSync)
            {
                statuses.Add(status);
            }
        };

        controller.Start(new InspectionPlcTriggerOptions
        {
            Enabled = true,
            Host = "127.0.0.1",
            Port = 502
        });

        await WaitUntilAsync(() => Snapshot(statuses, statusSync).Any(status =>
            status.Kind == InspectionTriggerStatusKind.Error &&
            status.Message.Contains("PLC\u8fde\u63a5\u5931\u8d25") &&
            status.Message.Contains("connect refused")));

        var status = Snapshot(statuses, statusSync).Last();
        Assert.Equal(InspectionTriggerStatusKind.Error, status.Kind);
        Assert.Contains("PLC\u8fde\u63a5\u5931\u8d25", status.Message);
        Assert.Contains("connect refused", status.Message);
    }

    [Fact]
    public async Task PlcTrigger_RetriesConnectionFailureAndRecovers()
    {
        var client = new FakeModbusClient();
        var statuses = new List<InspectionPlcTriggerStatus>();
        var statusSync = new object();
        var attempts = 0;
        var triggerCount = 0;
        var task = new InspectionTaskSessionViewModel(
            "task-instance-1",
            "Task 1",
            "appearance-check",
            "model-a",
            "p01",
            "station-1",
            InspectionActionTypes.RoiInspection,
            "appearance-check",
            InspectionTaskTriggerMode.TriggerCommand)
        {
            IsRunning = true
        };
        using var controller = new InspectionPlcTriggerController(
            (_, _) => task,
            _ =>
            {
                Interlocked.Increment(ref triggerCount);
                return Task.FromResult(new InspectionRuntimeTaskResult(
                    true,
                    InspectionCycleDecision.Ok,
                    "plc-trigger",
                    DateTimeOffset.Now));
            },
            _ =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new InvalidOperationException("connect refused");
                }

                return client;
            },
            TimeSpan.FromMilliseconds(20));
        controller.StatusChanged += (_, status) =>
        {
            lock (statusSync)
            {
                statuses.Add(status);
            }
        };

        controller.Start(new InspectionPlcTriggerOptions
        {
            Enabled = true,
            PollIntervalMs = 20,
            TriggerRegisterAddress = 30,
            DoneRegisterAddress = 31,
            ResultRegisterAddress = 32,
            TaskId = "Task 1"
        });

        await WaitUntilAsync(() => Snapshot(statuses, statusSync).Any(status =>
            status.Kind == InspectionTriggerStatusKind.Error &&
            status.Message.Contains("PLC\u8fde\u63a5\u5931\u8d25")));
        await WaitUntilAsync(() =>
            Volatile.Read(ref attempts) >= 2 &&
            Snapshot(statuses, statusSync).Any(status => status.Kind == InspectionTriggerStatusKind.Waiting));

        client.SetRegister(30, 1);
        await WaitUntilAsync(() =>
            Volatile.Read(ref triggerCount) == 1 &&
            client.GetRegister(31) == 1 &&
            client.GetRegister(32) == 1);

        Assert.Contains(Snapshot(statuses, statusSync), status => status.Kind == InspectionTriggerStatusKind.CompletedOk);
    }

    private static bool HasStatus(
        List<InspectionPlcTriggerStatus> statuses,
        object statusSync,
        InspectionTriggerStatusKind kind)
    {
        return Snapshot(statuses, statusSync).Any(status => status.Kind == kind);
    }

    private static InspectionPlcTriggerStatus[] Snapshot(
        List<InspectionPlcTriggerStatus> statuses,
        object statusSync)
    {
        lock (statusSync)
        {
            return statuses.ToArray();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(predicate());
    }

    private sealed class FakeModbusClient : IModbusRegisterClient
    {
        private readonly object _sync = new();
        private readonly Dictionary<ushort, ushort> _registers = new();

        public ushort GetRegister(ushort address)
        {
            lock (_sync)
            {
                return _registers.TryGetValue(address, out var value) ? value : (ushort)0;
            }
        }

        public void SetRegister(ushort address, ushort value)
        {
            lock (_sync)
            {
                _registers[address] = value;
            }
        }

        public Task<ushort[]> ReadHoldingRegistersAsync(byte slaveAddress, ushort startAddress, ushort numberOfPoints, CancellationToken ct)
        {
            lock (_sync)
            {
                var values = Enumerable.Range(0, numberOfPoints)
                    .Select(offset => GetRegister((ushort)(startAddress + offset)))
                    .ToArray();
                return Task.FromResult(values);
            }
        }

        public Task WriteSingleRegisterAsync(byte slaveAddress, ushort registerAddress, ushort value, CancellationToken ct)
        {
            SetRegister(registerAddress, value);
            return Task.CompletedTask;
        }

        public Task WriteMultipleRegistersAsync(byte slaveAddress, ushort startAddress, ushort[] values, CancellationToken ct)
        {
            for (var i = 0; i < values.Length; i++)
            {
                SetRegister((ushort)(startAddress + i), values[i]);
            }

            return Task.CompletedTask;
        }

        public Task WriteSingleCoilAsync(byte slaveAddress, ushort coilAddress, bool value, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task WriteMultipleCoilsAsync(byte slaveAddress, ushort startAddress, bool[] values, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
