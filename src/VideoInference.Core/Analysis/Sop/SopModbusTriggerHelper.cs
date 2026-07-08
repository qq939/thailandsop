namespace VideoInferenceDemo;

public sealed class SopModbusTriggerDefinition
{
    public ushort TriggerAddress { get; init; }
    public ushort TriggerValue { get; init; } = 1;
    public ushort? DoneAddress { get; init; }
    public ushort DoneValue { get; init; } = 1;
    public ushort ResultAddress { get; init; }
    public ushort OkValue { get; init; } = 1;
    public ushort NgValue { get; init; } = 2;
    public ushort ErrorValue { get; init; } = 4;
    public int TimeoutMs { get; init; } = 10000;

    public SopModbusTriggerDefinition Normalize()
    {
        return new SopModbusTriggerDefinition
        {
            TriggerAddress = TriggerAddress,
            TriggerValue = TriggerValue == 0 ? (ushort)1 : TriggerValue,
            DoneAddress = DoneAddress,
            DoneValue = DoneValue == 0 ? (ushort)1 : DoneValue,
            ResultAddress = ResultAddress,
            OkValue = OkValue == 0 ? (ushort)1 : OkValue,
            NgValue = NgValue == 0 ? (ushort)2 : NgValue,
            ErrorValue = ErrorValue == 0 ? (ushort)4 : ErrorValue,
            TimeoutMs = Math.Clamp(TimeoutMs <= 0 ? 10000 : TimeoutMs, 100, 600000)
        };
    }
}

public enum SopModbusTriggerState
{
    Pending,
    Ok,
    Ng,
    Timeout
}

public sealed record SopModbusTriggerEvaluation(
    SopModbusTriggerState State,
    ushort ResultValue,
    string DebugNote,
    string? NgReason)
{
    public bool IsOk => State == SopModbusTriggerState.Ok;
}

public static class SopModbusTriggerHelper
{
    public static SopModbusTriggerEvaluation Evaluate(
        AnalysisContext context,
        FsmFrameMetrics current,
        string key,
        SopModbusTriggerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(definition);

        var normalized = definition.Normalize();
        var nowUtcMs = current.FrameUtcMs > 0
            ? current.FrameUtcMs
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var state = context.State.GetOrCreateModbusTriggerState(key);

        if (!state.Triggered)
        {
            state.Triggered = true;
            state.StartedUtcMs = nowUtcMs;
            context.ModbusRegisters.WriteHoldingRegister(normalized.ResultAddress, 0);
            if (normalized.DoneAddress.HasValue)
            {
                context.ModbusRegisters.WriteHoldingRegister(normalized.DoneAddress.Value, 0);
            }

            context.ModbusRegisters.WriteHoldingRegister(normalized.TriggerAddress, normalized.TriggerValue);
        }

        if (state.CompletedState == SopModbusTriggerState.Ok ||
            state.CompletedState == SopModbusTriggerState.Ng ||
            state.CompletedState == SopModbusTriggerState.Timeout)
        {
            return BuildCompletedEvaluation(state.CompletedState.Value, state.LastResultValue, key, normalized);
        }

        if (normalized.DoneAddress.HasValue)
        {
            var done = context.ModbusRegisters.ReadHoldingRegister(normalized.DoneAddress.Value);
            var resultValue = context.ModbusRegisters.ReadHoldingRegister(normalized.ResultAddress);
            state.LastResultValue = resultValue;

            if (done != normalized.DoneValue)
            {
                if (nowUtcMs - state.StartedUtcMs >= normalized.TimeoutMs)
                {
                    context.ModbusRegisters.WriteHoldingRegister(normalized.TriggerAddress, 0);
                    state.CompletedState = SopModbusTriggerState.Timeout;
                    return BuildCompletedEvaluation(SopModbusTriggerState.Timeout, resultValue, key, normalized);
                }

                return new SopModbusTriggerEvaluation(
                    SopModbusTriggerState.Pending,
                    resultValue,
                    $"modbus_trigger_pending; key={key}; trigger={normalized.TriggerAddress}; done={normalized.DoneAddress.Value}; done_value={done}; result={normalized.ResultAddress}; value={resultValue}",
                    null);
            }

            if (resultValue == normalized.OkValue)
            {
                context.ModbusRegisters.WriteHoldingRegister(normalized.TriggerAddress, 0);
                state.CompletedState = SopModbusTriggerState.Ok;
                return BuildCompletedEvaluation(SopModbusTriggerState.Ok, resultValue, key, normalized);
            }

            context.ModbusRegisters.WriteHoldingRegister(normalized.TriggerAddress, 0);
            state.CompletedState = SopModbusTriggerState.Ng;
            return BuildCompletedEvaluation(SopModbusTriggerState.Ng, resultValue, key, normalized);
        }

        var result = context.ModbusRegisters.ReadHoldingRegister(normalized.ResultAddress);
        state.LastResultValue = result;
        if (result == normalized.OkValue)
        {
            context.ModbusRegisters.WriteHoldingRegister(normalized.TriggerAddress, 0);
            state.CompletedState = SopModbusTriggerState.Ok;
            return BuildCompletedEvaluation(SopModbusTriggerState.Ok, result, key, normalized);
        }

        if (result == normalized.NgValue)
        {
            context.ModbusRegisters.WriteHoldingRegister(normalized.TriggerAddress, 0);
            state.CompletedState = SopModbusTriggerState.Ng;
            return BuildCompletedEvaluation(SopModbusTriggerState.Ng, result, key, normalized);
        }

        if (nowUtcMs - state.StartedUtcMs >= normalized.TimeoutMs)
        {
            context.ModbusRegisters.WriteHoldingRegister(normalized.TriggerAddress, 0);
            state.CompletedState = SopModbusTriggerState.Timeout;
            return BuildCompletedEvaluation(SopModbusTriggerState.Timeout, result, key, normalized);
        }

        return new SopModbusTriggerEvaluation(
            SopModbusTriggerState.Pending,
            result,
            $"modbus_trigger_pending; key={key}; trigger={normalized.TriggerAddress}; result={normalized.ResultAddress}; value={result}",
            null);
    }

    public static void Reset(AnalysisContext context, string key)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        context.State.ModbusTriggerStates.Remove(key);
    }

    private static SopModbusTriggerEvaluation BuildCompletedEvaluation(
        SopModbusTriggerState state,
        ushort resultValue,
        string key,
        SopModbusTriggerDefinition definition)
    {
        return state switch
        {
            SopModbusTriggerState.Ok => new SopModbusTriggerEvaluation(
                state,
                resultValue,
                $"modbus_trigger_ok; key={key}; result={definition.ResultAddress}; value={resultValue}",
                null),
            SopModbusTriggerState.Ng => new SopModbusTriggerEvaluation(
                state,
                resultValue,
                $"modbus_trigger_ng; key={key}; result={definition.ResultAddress}; value={resultValue}",
                $"Modbus trigger '{key}' returned NG ({resultValue})."),
            SopModbusTriggerState.Timeout => new SopModbusTriggerEvaluation(
                state,
                resultValue,
                $"modbus_trigger_timeout; key={key}; result={definition.ResultAddress}; value={resultValue}",
                $"Modbus trigger '{key}' timed out after {definition.TimeoutMs} ms."),
            _ => new SopModbusTriggerEvaluation(state, resultValue, $"modbus_trigger_pending; key={key}", null)
        };
    }
}
