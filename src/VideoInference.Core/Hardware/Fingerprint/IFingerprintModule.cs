namespace VideoInferenceDemo;

public interface IFingerprintModule : IDisposable
{
    Task StartEnrollmentAsync(byte fingerprintId, CancellationToken ct);
    Task CancelAsync(CancellationToken ct);
    Task DeleteFingerprintAsync(byte fingerprintId, CancellationToken ct);
    Task ClearDatabaseAsync(CancellationToken ct);
    Task<FingerprintRecognitionResult> ReadRecognitionResultAsync(CancellationToken ct);
    Task<int> ReadTemplateCountAsync(CancellationToken ct);
    Task<FingerprintModuleStatus> ReadStatusAsync(CancellationToken ct);
    Task SetLightAsync(FingerprintLightMode mode, FingerprintLightColor color, CancellationToken ct);
    Task SetSlaveAddressAsync(byte address, CancellationToken ct);
}

public interface IModbusRegisterClient : IDisposable
{
    Task<ushort[]> ReadHoldingRegistersAsync(byte slaveAddress, ushort startAddress, ushort numberOfPoints, CancellationToken ct);
    Task WriteSingleRegisterAsync(byte slaveAddress, ushort registerAddress, ushort value, CancellationToken ct);
    Task WriteMultipleRegistersAsync(byte slaveAddress, ushort startAddress, ushort[] values, CancellationToken ct);
    Task WriteSingleCoilAsync(byte slaveAddress, ushort coilAddress, bool value, CancellationToken ct);
    Task WriteMultipleCoilsAsync(byte slaveAddress, ushort startAddress, bool[] values, CancellationToken ct);
}
