using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VideoInferenceDemo;

public sealed class ModbusIOController : IDisposable
{
    private readonly List<ModbusIOChannel> _channels = new();
    private readonly object _lock = new();
    private bool _started;

    public void Start(IEnumerable<ModbusModuleOptions> modules)
    {
        if (_started)
            return;

        var enabled = (modules ?? Enumerable.Empty<ModbusModuleOptions>())
            .Where(m => m.Enabled && m.Lights.Count > 0)
            .ToList();

        foreach (var module in enabled)
        {
            CameraDiagnostics.Info("modbus-io", $"Connecting to module '{module.Name}' at {module.Host}:{module.Port}...");
            try
            {
                var client = NModbusRegisterClient.CreateTcp(module.Host, module.Port);
                _channels.Add(new ModbusIOChannel(
                    client,
                    module.SlaveAddress,
                    module.OutputStartAddress,
                    module.PollIntervalMs,
                    module.Lights));
            }
            catch (Exception ex)
            {
                CameraDiagnostics.Error("modbus-io", $"Failed to connect module '{module.Name}' ({module.Host}:{module.Port}): {ex.Message}");
            }
        }

        _started = true;

        if (_channels.Count > 0)
        {
            CameraDiagnostics.Info("modbus-io", $"Started {_channels.Count} Modbus IO channel(s)");
        }
        else
        {
            CameraDiagnostics.Info("modbus-io", "No Modbus IO channels started — all connection attempts failed or no modules configured.");
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            foreach (var channel in _channels)
            {
                channel.Dispose();
            }

            _channels.Clear();
            _started = false;
        }
    }

    public void SetLightStates(bool isSystemRunning, bool isSopAlarmActive)
    {
        if (!_started)
            return;

        lock (_lock)
        {
            foreach (var channel in _channels)
            {
                channel.UpdateAsync(isSystemRunning, isSopAlarmActive);
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private sealed class ModbusIOChannel : IDisposable
    {
        private readonly NModbusRegisterClient _client;
        private readonly byte _slaveAddress;
        private readonly List<LightCoilSet> _lights;
        private readonly SemaphoreSlim _ioLock = new(1, 1);
        private readonly Timer _refreshTimer;
        private readonly CancellationTokenSource _cts = new();
        private volatile bool _desiredSystemRunning;
        private volatile bool _desiredSopAlarmActive;

        public ModbusIOChannel(
            NModbusRegisterClient client,
            byte slaveAddress,
            ushort outputStartAddress,
            int pollIntervalMs,
            List<ThreeColorLightBinding> lights)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _slaveAddress = slaveAddress;
            _lights = lights
                .Where(l => l != null)
                .Select(l =>
                {
                    var r = (ushort)(outputStartAddress + Math.Clamp(l.RedChannelNumber, 1, 16) - 1);
                    var g = (ushort)(outputStartAddress + Math.Clamp(l.GreenChannelNumber, 1, 16) - 1);
                    var b = (ushort)(outputStartAddress + Math.Clamp(l.BuzzerChannelNumber, 1, 16) - 1);
                    return new LightCoilSet
                    {
                        RedAddr = r,
                        GreenAddr = g,
                        BuzzerAddr = b,
                        BuzzerEnabled = l.BuzzerEnabled,
                    };
                })
                .ToList();
            var interval = Math.Clamp(pollIntervalMs, 100, 2000);
            _refreshTimer = new Timer(
                _ => UpdateAsync(_desiredSystemRunning, _desiredSopAlarmActive),
                null,
                interval,
                interval);
        }

        /// <summary>Fire-and-forget update; the channel also refreshes the desired output state periodically.</summary>
        public void UpdateAsync(bool isSystemRunning, bool isSopAlarmActive)
        {
            _desiredSystemRunning = isSystemRunning;
            _desiredSopAlarmActive = isSopAlarmActive;

            if (_cts.IsCancellationRequested)
                return;

            Task.Run(async () =>
            {
                try
                {
                    await UpdateCoreAsync(isSystemRunning, isSopAlarmActive, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Shutdown requested, ignore.
                }
                catch (ObjectDisposedException) when (_cts.IsCancellationRequested)
                {
                    // A restart can dispose the channel while a previous fire-and-forget write is still draining.
                }
                catch (Exception ex)
                {
                    CameraDiagnostics.Error("modbus-io", $"Channel write failed: {ex.Message}");
                }
            });
        }

        private async Task UpdateCoreAsync(bool isSystemRunning, bool isSopAlarmActive, CancellationToken ct)
        {
            await _ioLock.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                foreach (var light in _lights)
                {
                    bool redOn, greenOn, buzzerOn;

                    if (!isSystemRunning)
                    {
                        redOn = false;
                        greenOn = false;
                        buzzerOn = false;
                    }
                    else if (isSopAlarmActive)
                    {
                        redOn = true;
                        greenOn = false;
                        buzzerOn = light.BuzzerEnabled;
                    }
                    else
                    {
                        redOn = false;
                        greenOn = true;
                        buzzerOn = false;
                    }

                    await _client.WriteSingleCoilAsync(_slaveAddress, light.RedAddr, redOn, ct).ConfigureAwait(false);
                    await _client.WriteSingleCoilAsync(_slaveAddress, light.GreenAddr, greenOn, ct).ConfigureAwait(false);
                    await _client.WriteSingleCoilAsync(_slaveAddress, light.BuzzerAddr, buzzerOn, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public void Dispose()
        {
            if (_cts.IsCancellationRequested)
                return;

            _cts.Cancel();
            _refreshTimer.Dispose();
            _ioLock.Dispose();
            _client.Dispose();
            _cts.Dispose();
        }

        private sealed class LightCoilSet
        {
            public ushort RedAddr;
            public ushort GreenAddr;
            public ushort BuzzerAddr;
            public bool BuzzerEnabled;
        }
    }
}
