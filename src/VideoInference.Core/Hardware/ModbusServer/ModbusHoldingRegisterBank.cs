namespace VideoInferenceDemo;

public interface IModbusHoldingRegisterAccessor
{
    ushort ReadHoldingRegister(ushort address);
    ushort[] ReadHoldingRegisters(ushort startAddress, ushort numberOfPoints);
    void WriteHoldingRegister(ushort address, ushort value);
    void WriteHoldingRegisters(ushort startAddress, IReadOnlyList<ushort> values);
}

public sealed record ModbusHoldingRegisterChangedEventArgs(
    ushort Address,
    ushort OldValue,
    ushort NewValue);

public sealed record ModbusHoldingRegisterSnapshotItem(
    ushort Address,
    ushort Value);

public sealed class ModbusHoldingRegisterBank : IModbusHoldingRegisterAccessor
{
    private const int RegisterCount = ushort.MaxValue + 1;
    private readonly ushort[] _registers = new ushort[RegisterCount];
    private readonly object _lock = new();

    public event EventHandler<ModbusHoldingRegisterChangedEventArgs>? RegisterChanged;

    public ushort ReadHoldingRegister(ushort address)
    {
        lock (_lock)
        {
            return _registers[address];
        }
    }

    public ushort[] ReadHoldingRegisters(ushort startAddress, ushort numberOfPoints)
    {
        ValidateRange(startAddress, numberOfPoints);

        var values = new ushort[numberOfPoints];
        if (numberOfPoints == 0)
        {
            return values;
        }

        lock (_lock)
        {
            Array.Copy(_registers, startAddress, values, 0, numberOfPoints);
        }

        return values;
    }

    public IReadOnlyList<ModbusHoldingRegisterSnapshotItem> Snapshot(ushort startAddress, ushort numberOfPoints)
    {
        var values = ReadHoldingRegisters(startAddress, numberOfPoints);
        var items = new ModbusHoldingRegisterSnapshotItem[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            items[i] = new ModbusHoldingRegisterSnapshotItem((ushort)(startAddress + i), values[i]);
        }

        return items;
    }

    public void WriteHoldingRegister(ushort address, ushort value)
    {
        WriteHoldingRegisters(address, new[] { value });
    }

    public void WriteHoldingRegisters(ushort startAddress, IReadOnlyList<ushort> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(values), "Register count exceeds Modbus address space.");
        }

        ValidateRange(startAddress, (ushort)values.Count);

        List<ModbusHoldingRegisterChangedEventArgs>? changes = null;
        lock (_lock)
        {
            for (var i = 0; i < values.Count; i++)
            {
                var address = checked((ushort)(startAddress + i));
                var oldValue = _registers[address];
                var newValue = values[i];
                if (oldValue == newValue)
                {
                    continue;
                }

                _registers[address] = newValue;
                changes ??= new List<ModbusHoldingRegisterChangedEventArgs>();
                changes.Add(new ModbusHoldingRegisterChangedEventArgs(address, oldValue, newValue));
            }
        }

        if (changes == null)
        {
            return;
        }

        foreach (var change in changes)
        {
            RegisterChanged?.Invoke(this, change);
        }
    }

    private static void ValidateRange(ushort startAddress, ushort numberOfPoints)
    {
        if ((uint)startAddress + numberOfPoints > RegisterCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(numberOfPoints),
                numberOfPoints,
                "Modbus holding register range exceeds address 65535.");
        }
    }
}

public sealed class NullModbusHoldingRegisterAccessor : IModbusHoldingRegisterAccessor
{
    public static NullModbusHoldingRegisterAccessor Instance { get; } = new();

    private NullModbusHoldingRegisterAccessor()
    {
    }

    public ushort ReadHoldingRegister(ushort address) => 0;

    public ushort[] ReadHoldingRegisters(ushort startAddress, ushort numberOfPoints)
    {
        return new ushort[numberOfPoints];
    }

    public void WriteHoldingRegister(ushort address, ushort value)
    {
    }

    public void WriteHoldingRegisters(ushort startAddress, IReadOnlyList<ushort> values)
    {
    }
}
