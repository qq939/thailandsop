namespace VideoInferenceDemo;

public sealed class DbConfig
{
    public bool EnableRawDetections { get; set; }
    public bool EnableTcnFeatures { get; set; }
    public bool EnableTcnInference { get; set; }
    public int RetentionDays { get; set; } = 90;
    public bool EnableAutoCleanup { get; set; } = true;
    public ModbusTcpServerOptions ModbusTcpServer { get; set; } = new();

    public DbConfig Normalize()
    {
        return new DbConfig
        {
            EnableRawDetections = EnableRawDetections,
            EnableTcnFeatures = EnableTcnFeatures,
            EnableTcnInference = EnableTcnInference,
            RetentionDays = Math.Clamp(RetentionDays <= 0 ? 90 : RetentionDays, 1, 3650),
            EnableAutoCleanup = EnableAutoCleanup,
            ModbusTcpServer = (ModbusTcpServer ?? new ModbusTcpServerOptions()).Normalize()
        };
    }
}
