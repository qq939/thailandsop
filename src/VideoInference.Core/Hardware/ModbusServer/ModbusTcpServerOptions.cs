using System.Net;

namespace VideoInferenceDemo;

public sealed class ModbusTcpServerOptions
{
    public bool Enabled { get; set; }
    public string BindAddress { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 502;
    public byte UnitId { get; set; } = 1;

    public ModbusTcpServerOptions Normalize()
    {
        var address = string.IsNullOrWhiteSpace(BindAddress) ? "0.0.0.0" : BindAddress.Trim();
        if (!IPAddress.TryParse(address, out _))
        {
            address = "0.0.0.0";
        }

        return new ModbusTcpServerOptions
        {
            Enabled = Enabled,
            BindAddress = address,
            Port = Port is > 0 and <= 65535 ? Port : 502,
            UnitId = UnitId == 0 ? (byte)1 : UnitId
        };
    }
}
