namespace VideoInferenceDemo.Tests.Analysis;

public sealed class SopModbusTriggerHelperTests
{
    [Fact]
    public void Evaluate_WritesTriggerOnceAndCompletesOnOk()
    {
        var bank = new ModbusHoldingRegisterBank();
        var context = CreateContext(bank);
        var definition = new SopModbusTriggerDefinition
        {
            TriggerAddress = 100,
            TriggerValue = 7,
            ResultAddress = 101,
            OkValue = 1,
            NgValue = 2,
            TimeoutMs = 1000
        };

        var pending = SopModbusTriggerHelper.Evaluate(context, Frame(1000), "inspect", definition);
        bank.WriteHoldingRegister(100, 99);
        var stillPending = SopModbusTriggerHelper.Evaluate(context, Frame(1100), "inspect", definition);
        var triggerBeforeOk = bank.ReadHoldingRegister(100);
        bank.WriteHoldingRegister(101, 1);
        var ok = SopModbusTriggerHelper.Evaluate(context, Frame(1200), "inspect", definition);

        Assert.Equal(SopModbusTriggerState.Pending, pending.State);
        Assert.Equal(SopModbusTriggerState.Pending, stillPending.State);
        Assert.Equal(99, triggerBeforeOk);
        Assert.Equal(SopModbusTriggerState.Ok, ok.State);
        Assert.Equal(0, bank.ReadHoldingRegister(100));
        Assert.Null(ok.NgReason);
    }

    [Fact]
    public void Evaluate_ReturnsNgWhenResultMatchesNgValue()
    {
        var bank = new ModbusHoldingRegisterBank();
        var context = CreateContext(bank);
        var definition = CreateDefinition();

        SopModbusTriggerHelper.Evaluate(context, Frame(1000), "inspect", definition);
        bank.WriteHoldingRegister(101, 2);
        var result = SopModbusTriggerHelper.Evaluate(context, Frame(1100), "inspect", definition);

        Assert.Equal(SopModbusTriggerState.Ng, result.State);
        Assert.NotNull(result.NgReason);
        Assert.Equal(0, bank.ReadHoldingRegister(100));
    }

    [Fact]
    public void Evaluate_ReturnsTimeoutWhenResultDoesNotArrive()
    {
        var bank = new ModbusHoldingRegisterBank();
        var context = CreateContext(bank);
        var definition = CreateDefinition(timeoutMs: 100);

        SopModbusTriggerHelper.Evaluate(context, Frame(1000), "inspect", definition);
        var result = SopModbusTriggerHelper.Evaluate(context, Frame(1100), "inspect", definition);

        Assert.Equal(SopModbusTriggerState.Timeout, result.State);
        Assert.NotNull(result.NgReason);
        Assert.Equal(0, bank.ReadHoldingRegister(100));
    }

    [Fact]
    public void Reset_AllowsTriggerToRunAgain()
    {
        var bank = new ModbusHoldingRegisterBank();
        var context = CreateContext(bank);
        var definition = CreateDefinition();

        SopModbusTriggerHelper.Evaluate(context, Frame(1000), "inspect", definition);
        bank.WriteHoldingRegister(101, 1);
        SopModbusTriggerHelper.Evaluate(context, Frame(1100), "inspect", definition);
        SopModbusTriggerHelper.Reset(context, "inspect");
        var result = SopModbusTriggerHelper.Evaluate(context, Frame(1200), "inspect", definition);

        Assert.Equal(SopModbusTriggerState.Pending, result.State);
        Assert.Equal(1, bank.ReadHoldingRegister(100));
        Assert.Equal(0, bank.ReadHoldingRegister(101));
    }

    private static SopModbusTriggerDefinition CreateDefinition(int timeoutMs = 1000)
    {
        return new SopModbusTriggerDefinition
        {
            TriggerAddress = 100,
            ResultAddress = 101,
            TimeoutMs = timeoutMs
        };
    }

    private static AnalysisContext CreateContext(ModbusHoldingRegisterBank bank)
    {
        return new AnalysisContext(
            new AnalysisConfig(),
            Array.Empty<FsmStepDefinition>(),
            Array.Empty<AnalysisResult>(),
            new AnalysisState(),
            bank);
    }

    private static FsmFrameMetrics Frame(long utcMs)
    {
        return new FsmFrameMetrics
        {
            RunUuid = "run-1",
            SourceKey = "camera-1",
            TaskId = "task-1",
            FrameUtcMs = utcMs,
            PtsMs = utcMs
        };
    }
}
