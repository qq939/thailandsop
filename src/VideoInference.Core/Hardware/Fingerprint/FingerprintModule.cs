namespace VideoInferenceDemo;

public sealed class FingerprintModule : IFingerprintModule
{
    private readonly IModbusRegisterClient _client;
    private readonly byte _slaveAddress;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public FingerprintModule(IModbusRegisterClient client, byte slaveAddress = 1)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _slaveAddress = slaveAddress == 0 ? (byte)1 : slaveAddress;
    }

    public async Task StartEnrollmentAsync(byte fingerprintId, CancellationToken ct)
    {
        ValidateFingerprintId(fingerprintId);
        await WriteRegisterAsync(FingerprintModuleRegisters.StartEnrollment, fingerprintId, ct).ConfigureAwait(false);
    }

    public Task CancelAsync(CancellationToken ct)
    {
        return WriteRegisterAsync(FingerprintModuleRegisters.CancelOperation, FingerprintModuleRegisters.CancelValue, ct);
    }

    public async Task DeleteFingerprintAsync(byte fingerprintId, CancellationToken ct)
    {
        ValidateFingerprintId(fingerprintId);
        await WriteRegisterAsync(FingerprintModuleRegisters.DeleteFingerprint, fingerprintId, ct).ConfigureAwait(false);
    }

    public Task ClearDatabaseAsync(CancellationToken ct)
    {
        return WriteRegisterAsync(FingerprintModuleRegisters.ClearDatabase, FingerprintModuleRegisters.ClearDatabaseValue, ct);
    }

    public async Task<FingerprintRecognitionResult> ReadRecognitionResultAsync(CancellationToken ct)
    {
        var result = await ReadRegisterAsync(FingerprintModuleRegisters.RecognitionResult, ct).ConfigureAwait(false);
        if (result == FingerprintModuleRegisters.NoResultValue || result == 0)
        {
            return FingerprintRecognitionResult.Empty(result);
        }

        var score = await TryReadScoreAsync(ct).ConfigureAwait(false);
        if (result is >= 1 and <= 255)
        {
            return new FingerprintRecognitionResult(true, result, result, score, null);
        }

        return new FingerprintRecognitionResult(
            false,
            null,
            result,
            score,
            $"Fingerprint recognition failed with raw value 0x{result:X4}.");
    }

    public async Task<int> ReadTemplateCountAsync(CancellationToken ct)
    {
        return await ReadRegisterAsync(FingerprintModuleRegisters.TemplateCount, ct).ConfigureAwait(false);
    }

    public async Task<FingerprintModuleStatus> ReadStatusAsync(CancellationToken ct)
    {
        var value = await ReadRegisterAsync(FingerprintModuleRegisters.ModuleStatus, ct).ConfigureAwait(false);
        return value switch
        {
            1 => FingerprintModuleStatus.Enrollment,
            2 => FingerprintModuleStatus.Recognition,
            3 => FingerprintModuleStatus.InteractiveOperation,
            _ => throw new InvalidOperationException($"Unknown fingerprint module status: 0x{value:X4}.")
        };
    }

    public Task SetLightAsync(FingerprintLightMode mode, FingerprintLightColor color, CancellationToken ct)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown fingerprint light mode.");
        }

        if (!Enum.IsDefined(color))
        {
            throw new ArgumentOutOfRangeException(nameof(color), color, "Unknown fingerprint light color.");
        }

        var value = (ushort)(((int)mode << 8) | (int)color);
        return WriteRegisterAsync(FingerprintModuleRegisters.LightRing, value, ct);
    }

    public Task SetSlaveAddressAsync(byte address, CancellationToken ct)
    {
        if (address is < 1 or > 247)
        {
            throw new ArgumentOutOfRangeException(nameof(address), address, "Modbus address must be between 1 and 247.");
        }

        return WriteRegisterAsync(FingerprintModuleRegisters.SlaveAddress, address, ct);
    }

    public void Dispose()
    {
        _ioLock.Dispose();
        _client.Dispose();
    }

    private async Task<ushort> ReadRegisterAsync(ushort address, CancellationToken ct)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var values = await _client.ReadHoldingRegistersAsync(_slaveAddress, address, 1, ct).ConfigureAwait(false);
            if (values.Length == 0)
            {
                throw new InvalidOperationException($"Fingerprint module returned no data for register 0x{address:X4}.");
            }

            return values[0];
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private async Task WriteRegisterAsync(ushort address, ushort value, CancellationToken ct)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _client.WriteSingleRegisterAsync(_slaveAddress, address, value, ct).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private async Task<int?> TryReadScoreAsync(CancellationToken ct)
    {
        try
        {
            return await ReadRegisterAsync(FingerprintModuleRegisters.MatchScore, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException)
        {
            CameraDiagnostics.Error("fingerprint", "Failed to read fingerprint score.", ex);
            return null;
        }
    }

    private static void ValidateFingerprintId(byte fingerprintId)
    {
        if (fingerprintId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fingerprintId), fingerprintId, "Fingerprint id must be between 1 and 255.");
        }
    }
}
