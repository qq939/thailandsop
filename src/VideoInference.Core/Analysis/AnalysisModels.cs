using System;
using System.Collections.Generic;

namespace VideoInferenceDemo;

public readonly record struct FsmFrameMetrics
{
    public string RunUuid { get; init; }
    public long RunStartedUtcMs { get; init; }
    public string SourceKey { get; init; }
    public string TaskId { get; init; }
    public VisionTaskKind TaskKind { get; init; }
    public int FrameIndex { get; init; }
    public int FrameWidth { get; init; }
    public int FrameHeight { get; init; }
    public long PtsMs { get; init; }
    public long FrameUtcMs { get; init; }
    public FsmFrameFeatures Features { get; init; }
    public IReadOnlyList<DetectionEntity> Detections { get; init; }
}

public sealed class AnalysisState
{
    public int? ActiveStep { get; set; }
    public int HoldCounter { get; set; }
    public bool IsFaulted { get; set; }
    public string? FaultExpectedStateCode { get; set; }
    public string? FaultCurrentStateCode { get; set; }
    public string? FaultNgReason { get; set; }
    public Sop1State Sop1 { get; } = new();
    public Sop2State Sop2 { get; } = new();
    public Sop3State Sop3 { get; } = new();
    public Sop4State Sop4 { get; } = new();
    public Dictionary<string, AnalysisModbusTriggerRuntimeState> ModbusTriggerStates { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public AnalysisModbusTriggerRuntimeState GetOrCreateModbusTriggerState(string key)
    {
        if (!ModbusTriggerStates.TryGetValue(key, out var state))
        {
            state = new AnalysisModbusTriggerRuntimeState();
            ModbusTriggerStates[key] = state;
        }

        return state;
    }
}

public sealed class Sop1State
{
    public bool ProductAndPressSeen { get; set; }

    public void ResetCycle()
    {
        ProductAndPressSeen = false;
    }
}

public sealed class Sop2State
{
    public bool ExpansionSeen { get; set; }
    public float? PressBaselineDistance { get; set; }
    public bool PressMovedIn { get; set; }

    public void ResetCycle()
    {
        ExpansionSeen = false;
        PressBaselineDistance = null;
        PressMovedIn = false;
    }
}

public sealed class Sop3State
{
    public bool MovingLabelSeen { get; set; }

    public void ResetCycle()
    {
        MovingLabelSeen = false;
    }
}

public sealed class Sop4State
{
    public void ResetCycle() { }
}

public sealed class AnalysisModbusTriggerRuntimeState
{
    public bool Triggered { get; set; }
    public long StartedUtcMs { get; set; }
    public ushort LastResultValue { get; set; }
    public SopModbusTriggerState? CompletedState { get; set; }
}

public sealed class AnalysisResult
{
    public string RunUuid { get; init; } = string.Empty;
    public long RunStartedUtcMs { get; init; }
    public string SourceKey { get; init; } = string.Empty;
    public string TaskId { get; init; } = string.Empty;
    public string? StrategyName { get; init; }
    public int? Step { get; init; }
    public string? Label { get; init; }
    public double? Score { get; init; }
    public long FrameIndex { get; init; }
    public long PtsMs { get; init; }
    public long FrameUtcMs { get; init; }
    public string? DebugNote { get; init; }
    public bool IsTransition { get; init; }
    public bool IsReset { get; init; }
    public bool? TransitionOk { get; init; }
    public int? FromStep { get; init; }
    public int? ToStep { get; init; }
    public string? CurrentStateCode { get; init; }
    public string? ExpectedStateCode { get; init; }
    public string? NgReason { get; init; }
    public bool IsSopCycleReset { get; init; }
}

public sealed class AnalysisContext
{
    public AnalysisContext(
        AnalysisConfig config,
        IReadOnlyList<FsmStepDefinition> steps,
        IReadOnlyList<AnalysisResult> recentResults,
        AnalysisState state,
        IModbusHoldingRegisterAccessor? modbusRegisters = null)
    {
        Config = config;
        Steps = steps;
        RecentResults = recentResults;
        State = state;
        ModbusRegisters = modbusRegisters ?? NullModbusHoldingRegisterAccessor.Instance;
    }

    public AnalysisConfig Config { get; }
    public IReadOnlyList<FsmStepDefinition> Steps { get; }
    public IReadOnlyList<AnalysisResult> RecentResults { get; }
    public AnalysisState State { get; }
    public IModbusHoldingRegisterAccessor ModbusRegisters { get; }
}
