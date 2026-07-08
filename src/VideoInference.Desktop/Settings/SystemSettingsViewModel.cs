using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VideoInferenceDemo;

public sealed partial class SystemSettingsViewModel : ObservableObject, IDisposable
{
    private readonly HardwareSettingsRepository _hardwareSettingsRepository;
    private readonly IModbusHoldingRegisterAccessor _modbusRegisters;
    private readonly ModbusTcpServerHost? _modbusTcpServerHost;
    private readonly DispatcherTimer? _registerMonitorTimer;
    private readonly Dispatcher _dispatcher;
    private readonly DbConfig _dbConfig;
    private readonly AnalysisConfig _analysisConfig;

    public SystemSettingsViewModel(
        HardwareSettingsRepository hardwareSettingsRepository,
        IModbusHoldingRegisterAccessor? modbusRegisters = null,
        ModbusTcpServerHost? modbusTcpServerHost = null)
    {
        _hardwareSettingsRepository = hardwareSettingsRepository ?? throw new ArgumentNullException(nameof(hardwareSettingsRepository));
        _modbusRegisters = modbusRegisters ?? NullModbusHoldingRegisterAccessor.Instance;
        _modbusTcpServerHost = modbusTcpServerHost;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _dbConfig = _hardwareSettingsRepository.LoadDbConfig();
        _analysisConfig = _hardwareSettingsRepository.LoadAnalysisConfig();
        RetentionDaysText = _dbConfig.RetentionDays.ToString();
        EnableAutoCleanup = _dbConfig.EnableAutoCleanup;
        var serverOptions = (_dbConfig.ModbusTcpServer ?? new ModbusTcpServerOptions()).Normalize();
        ModbusTcpServerEnabled = serverOptions.Enabled;
        ModbusTcpServerBindAddress = serverOptions.BindAddress;
        ModbusTcpServerPortText = serverOptions.Port.ToString();
        UpdateModbusTcpServerStatus(_modbusTcpServerHost?.Status);

        foreach (var module in _hardwareSettingsRepository.LoadFingerprintModules().Select(FingerprintModuleSettingsItem.FromOptions))
        {
            FingerprintModules.Add(module);
        }

        foreach (var srcModule in _hardwareSettingsRepository.LoadModbusModules())
        {
            var normalized = srcModule.Normalize();
            var settingsItem = ModbusModuleSettingsItem.FromOptions(normalized);
            ModbusModules.Add(settingsItem);

            foreach (var light in normalized.Lights)
            {
                LightBindings.Add(LightBindingSettingsItem.FromOptions(light, normalized.Id, normalized.Name));
            }
        }

        RefreshRegisterMonitor();
        if (_modbusTcpServerHost != null)
        {
            _modbusTcpServerHost.StatusChanged += OnModbusTcpServerStatusChanged;
        }

        _registerMonitorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _registerMonitorTimer.Tick += (_, _) => RefreshRegisterMonitor();
        _registerMonitorTimer.Start();
    }

    public string ConfigPath => "workspace_config.db";
    public ObservableCollection<FingerprintModuleSettingsItem> FingerprintModules { get; } = new();
    public ObservableCollection<ModbusModuleSettingsItem> ModbusModules { get; } = new();
    public ObservableCollection<LightBindingSettingsItem> LightBindings { get; } = new();
    public ObservableCollection<ModbusRegisterMonitorItem> ModbusRegisterMonitorItems { get; } = new();

    [ObservableProperty]
    private string retentionDaysText = "90";

    [ObservableProperty]
    private bool enableAutoCleanup = true;

    [ObservableProperty]
    private bool modbusTcpServerEnabled;

    [ObservableProperty]
    private string modbusTcpServerBindAddress = "0.0.0.0";

    [ObservableProperty]
    private string modbusTcpServerPortText = "502";

    [ObservableProperty]
    private string modbusTcpServerStatusText = "Disabled";

    [ObservableProperty]
    private string registerMonitorStartAddressText = "0";

    [ObservableProperty]
    private string errorMessage = string.Empty;

    partial void OnRegisterMonitorStartAddressTextChanged(string value)
    {
        RefreshRegisterMonitor();
    }

    [RelayCommand]
    private void AddFingerprintModule()
    {
        FingerprintModules.Add(FingerprintModuleSettingsItem.FromOptions(new FingerprintModuleOptions
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"指纹模块 {FingerprintModules.Count + 1}",
            Enabled = true
        }));
    }

    [RelayCommand]
    private void RemoveFingerprintModule(FingerprintModuleSettingsItem item)
    {
        FingerprintModules.Remove(item);
    }

    [RelayCommand]
    private void AddModbusModule()
    {
        var module = ModbusModuleSettingsItem.FromOptions(new ModbusModuleOptions
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"Modbus IO 模块 {ModbusModules.Count + 1}",
            Enabled = true
        });
        ModbusModules.Add(module);
    }

    [RelayCommand]
    private void RemoveModbusModule(ModbusModuleSettingsItem item)
    {
        // Remove all light bindings referencing this module
        var bindingsToRemove = LightBindings.Where(b => b.ModuleId == item.Id).ToList();
        foreach (var b in bindingsToRemove)
        {
            LightBindings.Remove(b);
        }

        ModbusModules.Remove(item);
    }

    [RelayCommand]
    private void AddLightBinding()
    {
        var firstModule = ModbusModules.FirstOrDefault();
        if (firstModule == null)
            return;

        LightBindings.Add(new LightBindingSettingsItem
        {
            ModuleId = firstModule.Id,
            ModuleName = firstModule.Name,
            LightNumber = 1,
            RedChannelText = "1",
            GreenChannelText = "2",
            BuzzerChannelText = "3",
            BuzzerEnabled = true
        });
    }

    [RelayCommand]
    private void RemoveLightBinding(LightBindingSettingsItem item)
    {
        LightBindings.Remove(item);
    }

    public bool TrySave()
    {
        ErrorMessage = string.Empty;

        if (!TryBuildFingerprintModules(out var fingerprintModules, out var error))
        {
            ErrorMessage = error;
            return false;
        }

        if (!TryBuildModbusModules(out var modbusModules, out error))
        {
            ErrorMessage = error;
            return false;
        }

        if (!TryBuildModbusTcpServerOptions(out var modbusTcpServer, out error))
        {
            ErrorMessage = error;
            return false;
        }

        if (!int.TryParse(RetentionDaysText, out var retentionDays) ||
            retentionDays < 1 ||
            retentionDays > 3650)
        {
            ErrorMessage = "Retention days must be between 1 and 3650.";
            return false;
        }

        try
        {
            _hardwareSettingsRepository.Save(fingerprintModules, modbusModules);
            _hardwareSettingsRepository.SaveGlobalConfig(
                new DbConfig
                {
                    EnableRawDetections = _dbConfig.EnableRawDetections,
                    EnableTcnFeatures = _dbConfig.EnableTcnFeatures,
                    EnableTcnInference = _dbConfig.EnableTcnInference,
                    RetentionDays = retentionDays,
                    EnableAutoCleanup = EnableAutoCleanup,
                    ModbusTcpServer = modbusTcpServer
                },
                _analysisConfig);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存系统设置失败：{ex.Message}";
            return false;
        }
    }

    public void Dispose()
    {
        _registerMonitorTimer?.Stop();
        if (_modbusTcpServerHost != null)
        {
            _modbusTcpServerHost.StatusChanged -= OnModbusTcpServerStatusChanged;
        }
    }

    private bool TryBuildFingerprintModules(out List<FingerprintModuleOptions> modules, out string error)
    {
        modules = new List<FingerprintModuleOptions>();
        error = string.Empty;

        foreach (var item in FingerprintModules)
        {
            if (!item.TryBuild(out var module, out error))
            {
                return false;
            }

            modules.Add(module);
        }

        return true;
    }

    private bool TryBuildModbusModules(out List<ModbusModuleOptions> modules, out string error)
    {
        modules = new List<ModbusModuleOptions>();
        error = string.Empty;

        foreach (var item in ModbusModules)
        {
            if (!item.TryBuild(out var module, out error))
            {
                return false;
            }

            // Attach light bindings for this module
            var lights = LightBindings
                .Where(b => b.ModuleId == item.Id)
                .Select(b =>
                {
                    var light = b.TryBuild(out _);
                    return light;
                })
                .Where(l => l != null)
                .ToList();

            module.Lights = lights!;
            modules.Add(module);
        }

        return true;
    }

    private bool TryBuildModbusTcpServerOptions(out ModbusTcpServerOptions options, out string error)
    {
        options = new ModbusTcpServerOptions();
        error = string.Empty;

        var address = string.IsNullOrWhiteSpace(ModbusTcpServerBindAddress)
            ? "0.0.0.0"
            : ModbusTcpServerBindAddress.Trim();
        if (!IPAddress.TryParse(address, out _))
        {
            error = $"ModbusTCP 服务端监听地址无效：{address}";
            return false;
        }

        if (!int.TryParse(ModbusTcpServerPortText, out var port) || port <= 0 || port > 65535)
        {
            error = $"ModbusTCP 服务端端口无效：{ModbusTcpServerPortText}";
            return false;
        }

        options = new ModbusTcpServerOptions
        {
            Enabled = ModbusTcpServerEnabled,
            BindAddress = address,
            Port = port,
            UnitId = 1
        }.Normalize();
        return true;
    }

    [RelayCommand]
    private void RefreshRegisterMonitor()
    {
        if (!ushort.TryParse(RegisterMonitorStartAddressText, out var startAddress) ||
            startAddress > ushort.MaxValue - 9)
        {
            return;
        }

        var snapshot = _modbusRegisters.ReadHoldingRegisters(startAddress, 10);
        ModbusRegisterMonitorItems.Clear();
        for (var i = 0; i < snapshot.Length; i++)
        {
            var address = (ushort)(startAddress + i);
            ModbusRegisterMonitorItems.Add(new ModbusRegisterMonitorItem(address, snapshot[i]));
        }
    }

    private void OnModbusTcpServerStatusChanged(object? sender, ModbusTcpServerRuntimeStatus e)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => UpdateModbusTcpServerStatus(e));
            return;
        }

        UpdateModbusTcpServerStatus(e);
    }

    private void UpdateModbusTcpServerStatus(ModbusTcpServerRuntimeStatus? status)
    {
        if (status == null || !status.Enabled)
        {
            ModbusTcpServerStatusText = "未启用";
            return;
        }

        ModbusTcpServerStatusText = status.IsRunning
            ? $"运行中：{status.Endpoint}"
            : $"异常：{status.Message}";
    }
}

public sealed class ModbusRegisterMonitorItem
{
    public ModbusRegisterMonitorItem(ushort address, ushort value)
    {
        Address = address;
        Value = value;
    }

    public ushort Address { get; }
    public ushort Value { get; }
    public string HexValue => $"0x{Value:X4}";
}

public sealed partial class FingerprintModuleSettingsItem : ObservableObject
{
    [ObservableProperty]
    private bool enabled;

    [ObservableProperty]
    private string id = string.Empty;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string connectionKind = FingerprintConnectionKind.SerialRtu.ToString();

    [ObservableProperty]
    private string slaveAddressText = "1";

    [ObservableProperty]
    private string portName = "COM3";

    [ObservableProperty]
    private string baudRateText = "9600";

    [ObservableProperty]
    private string dataBitsText = "8";

    [ObservableProperty]
    private string parity = "None";

    [ObservableProperty]
    private string stopBitsText = "1";

    [ObservableProperty]
    private string host = "127.0.0.1";

    [ObservableProperty]
    private string tcpPortText = "502";

    [ObservableProperty]
    private string pollIntervalMsText = "200";

    public static FingerprintModuleSettingsItem FromOptions(FingerprintModuleOptions options)
    {
        var normalized = (options ?? new FingerprintModuleOptions()).Normalize();
        return new FingerprintModuleSettingsItem
        {
            Enabled = normalized.Enabled,
            Id = normalized.Id,
            Name = normalized.Name,
            ConnectionKind = normalized.ConnectionKind.ToString(),
            SlaveAddressText = normalized.SlaveAddress.ToString(),
            PortName = normalized.PortName,
            BaudRateText = normalized.BaudRate.ToString(),
            DataBitsText = normalized.DataBits.ToString(),
            Parity = normalized.Parity,
            StopBitsText = normalized.StopBits.ToString(),
            Host = normalized.Host,
            TcpPortText = normalized.TcpPort.ToString(),
            PollIntervalMsText = normalized.PollIntervalMs.ToString()
        };
    }

    public bool TryBuild(out FingerprintModuleOptions options, out string error)
    {
        options = new FingerprintModuleOptions();
        error = string.Empty;

        if (!Enum.TryParse<FingerprintConnectionKind>(ConnectionKind, ignoreCase: true, out var connectionKind))
        {
            error = $"指纹模块连接类型无效：{ConnectionKind}";
            return false;
        }

        if (!byte.TryParse(SlaveAddressText, out var slaveAddress) || slaveAddress == 0)
        {
            error = $"指纹模块站号无效：{Name}";
            return false;
        }

        if (!int.TryParse(BaudRateText, out var baudRate) || baudRate <= 0 ||
            !int.TryParse(DataBitsText, out var dataBits) || dataBits <= 0 ||
            !int.TryParse(StopBitsText, out var stopBits) || stopBits <= 0 ||
            !int.TryParse(TcpPortText, out var tcpPort) || tcpPort <= 0 ||
            !int.TryParse(PollIntervalMsText, out var pollIntervalMs) || pollIntervalMs <= 0)
        {
            error = $"指纹模块串口/TCP参数无效：{Name}";
            return false;
        }

        options = new FingerprintModuleOptions
        {
            Enabled = Enabled,
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim(),
            Name = string.IsNullOrWhiteSpace(Name) ? "指纹模块" : Name.Trim(),
            ConnectionKind = connectionKind,
            SlaveAddress = slaveAddress,
            PortName = PortName,
            BaudRate = baudRate,
            DataBits = dataBits,
            Parity = Parity,
            StopBits = stopBits,
            Host = Host,
            TcpPort = tcpPort,
            PollIntervalMs = pollIntervalMs
        }.Normalize();
        return true;
    }
}

public sealed partial class ModbusModuleSettingsItem : ObservableObject
{
    [ObservableProperty]
    private bool enabled;

    [ObservableProperty]
    private string id = string.Empty;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string host = "127.0.0.1";

    [ObservableProperty]
    private string portText = "502";

    [ObservableProperty]
    private string slaveAddressText = "1";

    [ObservableProperty]
    private string pollIntervalMsText = "500";

    [ObservableProperty]
    private string outputStartAddressText = "0";

    [ObservableProperty]
    private string inputStartAddressText = "0";

    public static ModbusModuleSettingsItem FromOptions(ModbusModuleOptions options)
    {
        var normalized = (options ?? new ModbusModuleOptions()).Normalize();
        return new ModbusModuleSettingsItem
        {
            Enabled = normalized.Enabled,
            Id = normalized.Id,
            Name = normalized.Name,
            Host = normalized.Host,
            PortText = normalized.Port.ToString(),
            SlaveAddressText = normalized.SlaveAddress.ToString(),
            PollIntervalMsText = normalized.PollIntervalMs.ToString(),
            OutputStartAddressText = normalized.OutputStartAddress.ToString(),
            InputStartAddressText = normalized.InputStartAddress.ToString()
        };
    }

    public bool TryBuild(out ModbusModuleOptions options, out string error)
    {
        options = new ModbusModuleOptions();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(Host))
        {
            error = $"Modbus IO 模块地址无效：{Name}";
            return false;
        }

        if (!int.TryParse(PortText, out var port) || port <= 0 || port > 65535)
        {
            error = $"Modbus IO 模块端口无效：{Name}";
            return false;
        }

        if (!byte.TryParse(SlaveAddressText, out var slaveAddress) || slaveAddress == 0)
        {
            error = $"Modbus IO 模块站号无效：{Name}";
            return false;
        }

        if (!int.TryParse(PollIntervalMsText, out var pollIntervalMs) || pollIntervalMs <= 0)
        {
            error = $"Modbus IO 模块轮询间隔无效：{Name}";
            return false;
        }

        ushort.TryParse(OutputStartAddressText, out var outputAddr);
        ushort.TryParse(InputStartAddressText, out var inputAddr);

        options = new ModbusModuleOptions
        {
            Enabled = Enabled,
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim(),
            Name = string.IsNullOrWhiteSpace(Name) ? "Modbus IO 模块" : Name.Trim(),
            Host = Host.Trim(),
            Port = port,
            SlaveAddress = slaveAddress,
            PollIntervalMs = pollIntervalMs,
            OutputStartAddress = outputAddr,
            InputStartAddress = inputAddr
        }.Normalize();
        return true;
    }
}

public sealed partial class LightBindingSettingsItem : ObservableObject
{
    [ObservableProperty]
    private string moduleId = string.Empty;

    [ObservableProperty]
    private string moduleName = string.Empty;

    [ObservableProperty]
    private int lightNumber = 1;

    [ObservableProperty]
    private string redChannelText = "1";

    [ObservableProperty]
    private string greenChannelText = "2";

    [ObservableProperty]
    private string buzzerChannelText = "3";

    [ObservableProperty]
    private bool buzzerEnabled = true;

    public static LightBindingSettingsItem FromOptions(ThreeColorLightBinding binding, string moduleId, string moduleName)
    {
        return new LightBindingSettingsItem
        {
            ModuleId = moduleId,
            ModuleName = moduleName,
            LightNumber = binding.LightNumber,
            RedChannelText = binding.RedChannelNumber.ToString(),
            GreenChannelText = binding.GreenChannelNumber.ToString(),
            BuzzerChannelText = binding.BuzzerChannelNumber.ToString(),
            BuzzerEnabled = binding.BuzzerEnabled
        };
    }

    public ThreeColorLightBinding? TryBuild(out string error)
    {
        error = string.Empty;

        if (!int.TryParse(RedChannelText, out var redCh) || redCh < 1 || redCh > 16)
        {
            error = $"红灯通道号无效：{RedChannelText}（1~16）";
            return null;
        }

        if (!int.TryParse(GreenChannelText, out var greenCh) || greenCh < 1 || greenCh > 16)
        {
            error = $"绿灯通道号无效：{GreenChannelText}（1~16）";
            return null;
        }

        if (!int.TryParse(BuzzerChannelText, out var buzzerCh) || buzzerCh < 1 || buzzerCh > 16)
        {
            error = $"蜂鸣器通道号无效：{BuzzerChannelText}（1~16）";
            return null;
        }

        return new ThreeColorLightBinding
        {
            LightNumber = LightNumber is 1 or 2 ? LightNumber : 1,
            RedChannelNumber = redCh,
            GreenChannelNumber = greenCh,
            BuzzerChannelNumber = buzzerCh,
            BuzzerEnabled = BuzzerEnabled
        };
    }
}
