namespace VideoInferenceDemo;

public enum FingerprintModuleStatus
{
    Enrollment = 1,
    Recognition = 2,
    InteractiveOperation = 3
}

public enum FingerprintLightMode
{
    Breathing = 0x01,
    Flashing = 0x02,
    AlwaysOn = 0x03,
    AlwaysOff = 0x04,
    FadeOn = 0x05,
    FadeOff = 0x06
}

public enum FingerprintLightColor
{
    Red = 0x01,
    Blue = 0x02,
    Purple = 0x03
}

public enum FingerprintConnectionKind
{
    SerialRtu,
    Tcp
}

public sealed record FingerprintRecognitionResult(
    bool Success,
    int? FingerprintId,
    int RawValue,
    int? Score,
    string? FailureReason)
{
    public static FingerprintRecognitionResult Empty(ushort rawValue = 0xFFFF)
    {
        return new FingerprintRecognitionResult(false, null, rawValue, null, "No current fingerprint result.");
    }
}

public sealed record FingerprintPersonnelRecognition(
    string ModuleId,
    string ModuleName,
    FingerprintRecognitionResult Result,
    PersonnelRecord? Personnel,
    long RecognizedUtcMs);

public sealed class FingerprintModuleOptions
{
    public bool Enabled { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Fingerprint Module";
    public FingerprintConnectionKind ConnectionKind { get; set; } = FingerprintConnectionKind.SerialRtu;
    public byte SlaveAddress { get; set; } = 1;
    public string PortName { get; set; } = "COM1";
    public int BaudRate { get; set; } = 9600;
    public int DataBits { get; set; } = 8;
    public string Parity { get; set; } = "None";
    public int StopBits { get; set; } = 1;
    public string Host { get; set; } = "127.0.0.1";
    public int TcpPort { get; set; } = 502;
    public int ReadTimeoutMs { get; set; } = 1000;
    public int WriteTimeoutMs { get; set; } = 1000;
    public int PollIntervalMs { get; set; } = 200;
    public int DuplicateSuppressMs { get; set; } = 3000;

    public FingerprintModuleOptions Normalize()
    {
        return new FingerprintModuleOptions
        {
            Enabled = Enabled,
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim(),
            Name = string.IsNullOrWhiteSpace(Name) ? "Fingerprint Module" : Name.Trim(),
            ConnectionKind = Enum.IsDefined(ConnectionKind) ? ConnectionKind : FingerprintConnectionKind.SerialRtu,
            SlaveAddress = SlaveAddress == 0 ? (byte)1 : SlaveAddress,
            PortName = string.IsNullOrWhiteSpace(PortName) ? "COM1" : PortName.Trim(),
            BaudRate = BaudRate > 0 ? BaudRate : 9600,
            DataBits = DataBits > 0 ? DataBits : 8,
            Parity = string.IsNullOrWhiteSpace(Parity) ? "None" : Parity.Trim(),
            StopBits = StopBits is 1 or 2 ? StopBits : 1,
            Host = string.IsNullOrWhiteSpace(Host) ? "127.0.0.1" : Host.Trim(),
            TcpPort = TcpPort > 0 ? TcpPort : 502,
            ReadTimeoutMs = ReadTimeoutMs > 0 ? ReadTimeoutMs : 1000,
            WriteTimeoutMs = WriteTimeoutMs > 0 ? WriteTimeoutMs : 1000,
            PollIntervalMs = Math.Clamp(PollIntervalMs, 100, 2000),
            DuplicateSuppressMs = Math.Max(0, DuplicateSuppressMs)
        };
    }
}
