using System;
using System.Collections.Generic;
using System.Linq;

namespace VideoInferenceDemo;

public sealed class ThreeColorLightBinding
{
    public int LightNumber { get; set; } = 1;

    /// <summary>红灯使用的输出通道号 1~16</summary>
    public int RedChannelNumber { get; set; } = 1;

    /// <summary>绿灯使用的输出通道号 1~16</summary>
    public int GreenChannelNumber { get; set; } = 2;

    /// <summary>蜂鸣器使用的输出通道号 1~16</summary>
    public int BuzzerChannelNumber { get; set; } = 3;

    /// <summary>蜂鸣器是否启用</summary>
    public bool BuzzerEnabled { get; set; } = true;
}

public sealed class ModbusModuleOptions
{
    public bool Enabled { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Modbus IO 模块";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 502;
    public byte SlaveAddress { get; set; } = 1;
    public int PollIntervalMs { get; set; } = 500;

    /// <summary>输出通道起始 Modbus 地址（三色灯使用）</summary>
    public ushort OutputStartAddress { get; set; }

    /// <summary>输入通道起始 Modbus 地址（预留）</summary>
    public ushort InputStartAddress { get; set; }

    public List<ThreeColorLightBinding> Lights { get; set; } = new();

    public ModbusModuleOptions Normalize()
    {
        return new ModbusModuleOptions
        {
            Enabled = Enabled,
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim(),
            Name = string.IsNullOrWhiteSpace(Name) ? "Modbus IO 模块" : Name.Trim(),
            Host = string.IsNullOrWhiteSpace(Host) ? "127.0.0.1" : Host.Trim(),
            Port = Port > 0 ? Port : 502,
            SlaveAddress = SlaveAddress == 0 ? (byte)1 : SlaveAddress,
            PollIntervalMs = Math.Clamp(PollIntervalMs, 100, 2000),
            OutputStartAddress = OutputStartAddress,
            InputStartAddress = InputStartAddress,
            Lights = (Lights ?? new List<ThreeColorLightBinding>())
                .Where(l => l != null)
                .Select(l => new ThreeColorLightBinding
                {
                    LightNumber = l.LightNumber is 1 or 2 ? l.LightNumber : 1,
                    RedChannelNumber = Math.Clamp(l.RedChannelNumber, 1, 16),
                    GreenChannelNumber = Math.Clamp(l.GreenChannelNumber, 1, 16),
                    BuzzerChannelNumber = Math.Clamp(l.BuzzerChannelNumber, 1, 16),
                    BuzzerEnabled = l.BuzzerEnabled
                })
                .ToList()
        };
    }
}
