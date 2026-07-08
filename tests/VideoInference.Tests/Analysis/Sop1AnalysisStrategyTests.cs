namespace VideoInferenceDemo.Tests.Analysis;

public sealed class Sop1AnalysisStrategyTests
{
    [Fact]
    public void Analyze_CompletesNormalFlowAndResetsForNextCycle()
    {
        var bank = new ModbusHoldingRegisterBank();
        var engine = CreateEngine(bank);
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;
        var frame = 0;

        CompleteInspectPosition(engine, ref frame);
        Assert.Equal(1, results[^1].Step);
        Assert.Equal("sop1:inspection_connector_ready", results[^1].CurrentStateCode);

        Enqueue(engine, ref frame);
        Assert.Equal((ushort)1, bank.ReadHoldingRegister(10));
        Assert.Equal((ushort)0, bank.ReadHoldingRegister(11));
        Assert.Equal((ushort)0, bank.ReadHoldingRegister(12));

        bank.WriteHoldingRegister(11, 1);
        bank.WriteHoldingRegister(12, 1);
        Enqueue(engine, ref frame);
        Assert.Equal(2, results[^1].Step);
        Assert.Equal("sop1:visual_inspection_ok", results[^1].CurrentStateCode);
        Assert.Equal((ushort)0, bank.ReadHoldingRegister(10));

        CompleteLubrication(engine, ref frame);
        Assert.Equal(3, results[^1].Step);
        Assert.Equal("sop1:lubrication_ready", results[^1].CurrentStateCode);

        CompleteTightenNylonTube(engine, ref frame);
        Assert.Equal(4, results[^1].Step);
        Assert.Equal("sop1:product_ready", results[^1].CurrentStateCode);

        CompleteAutoFlaringInsertion(engine, ref frame);
        Assert.Equal(5, results[^1].Step);
        Assert.Equal("sop1:auto_flaring_inserted", results[^1].CurrentStateCode);

        Enqueue(engine, ref frame);
        Assert.Null(results[^1].Step);
        Assert.True(results[^1].IsReset);
        Assert.Equal("sop1:inspection_connector_ready", results[^1].ExpectedStateCode);
    }

    [Fact]
    public void Analyze_RaisesNgWhenProductAppearsBeforeInspectionConnector()
    {
        var engine = CreateEngine();
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;
        var frame = 0;

        for (var i = 0; i < 5; i++)
        {
            Enqueue(engine, ref frame, Detection(0, "product"));
        }

        Assert.Equal("ng:sop1_product_before_inspection", results[^1].CurrentStateCode);
        Assert.Equal(1, results[^1].Step);
        Assert.NotNull(results[^1].NgReason);
        Assert.False(results[^1].IsTransition);
        Assert.False(results[^1].IsReset);

        CompleteInspectPosition(engine, ref frame);
        Assert.Equal("ng:sop1_product_before_inspection", results[^1].CurrentStateCode);
        Assert.Equal(1, results[^1].Step);
        Assert.Equal("sop1_fault_locked", results[^1].DebugNote);
    }

    [Fact]
    public void Analyze_ModbusNgAndErrorValuesFaultAndClearTrigger()
    {
        var ngBank = new ModbusHoldingRegisterBank();
        var ngEngine = CreateEngine(ngBank);
        var ngResults = new List<AnalysisResult>();
        ngEngine.ResultReady += ngResults.Add;
        var ngFrame = 0;
        CompleteInspectPosition(ngEngine, ref ngFrame);
        Enqueue(ngEngine, ref ngFrame);
        ngBank.WriteHoldingRegister(11, 1);
        ngBank.WriteHoldingRegister(12, 2);
        Enqueue(ngEngine, ref ngFrame);

        Assert.Equal("ng:sop1_visual_inspection", ngResults[^1].CurrentStateCode);
        Assert.Equal(2, ngResults[^1].Step);
        Assert.Equal((ushort)0, ngBank.ReadHoldingRegister(10));

        var errorBank = new ModbusHoldingRegisterBank();
        var errorEngine = CreateEngine(errorBank);
        var errorResults = new List<AnalysisResult>();
        errorEngine.ResultReady += errorResults.Add;
        var errorFrame = 0;
        CompleteInspectPosition(errorEngine, ref errorFrame);
        Enqueue(errorEngine, ref errorFrame);
        errorBank.WriteHoldingRegister(11, 1);
        errorBank.WriteHoldingRegister(12, 4);
        Enqueue(errorEngine, ref errorFrame);

        Assert.Equal("ng:sop1_visual_inspection", errorResults[^1].CurrentStateCode);
        Assert.Equal(2, errorResults[^1].Step);
        Assert.Equal((ushort)0, errorBank.ReadHoldingRegister(10));
    }

    [Fact]
    public void Analyze_ModbusTimeoutFaultsAndClearsTrigger()
    {
        var bank = new ModbusHoldingRegisterBank();
        var engine = CreateEngine(bank);
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;
        var frame = 0;

        CompleteInspectPosition(engine, ref frame);
        Enqueue(engine, ref frame);
        Enqueue(engine, ref frame, ptsIncrementMs: 10050);

        Assert.Equal("ng:sop1_visual_inspection_timeout", results[^1].CurrentStateCode);
        Assert.Equal(2, results[^1].Step);
        Assert.Equal((ushort)0, bank.ReadHoldingRegister(10));
    }

    [Fact]
    public void Analyze_RaisesNgWhenProductAppearsBeforeLubrication()
    {
        var engine = CreateEngine();
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;
        var frame = 0;

        CompleteThroughVisionInspection(engine, ref frame);
        Enqueue(engine, ref frame, Detection(0, "product"));
        Enqueue(engine, ref frame, Detection(0, "product"));
        Enqueue(engine, ref frame, Detection(0, "product"));

        Assert.Equal("ng:sop1_product_before_lubrication", results[^1].CurrentStateCode);
        Assert.Equal(3, results[^1].Step);
        Assert.NotNull(results[^1].NgReason);

        CompleteLubrication(engine, ref frame);
        Assert.Equal("ng:sop1_product_before_lubrication", results[^1].CurrentStateCode);
        Assert.Equal(3, results[^1].Step);
        Assert.Equal("sop1_fault_locked", results[^1].DebugNote);
    }

    [Fact]
    public void Analyze_CompletesStep5AfterProductAndPressThenProductDisappears()
    {
        var engine = CreateEngine();
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;
        var frame = 0;

        CompleteThroughTightenNylonTube(engine, ref frame);

        Enqueue(engine, ref frame, Detection(0, "product"), Detection(1, "press"));
        Enqueue(engine, ref frame, Detection(0, "product"), Detection(1, "press"));
        Enqueue(engine, ref frame, Detection(0, "product"), Detection(1, "press"));

        Assert.Equal(4, results[^1].Step);
        Assert.Contains("product_and_press_seen", results[^1].DebugNote);

        for (var i = 0; i < 5; i++)
        {
            Enqueue(engine, ref frame);
        }

        Assert.Equal(5, results[^1].Step);
        Assert.Equal("sop1:auto_flaring_inserted", results[^1].CurrentStateCode);
    }

    [Fact]
    public void Analyze_MatchesSop1ClassesByIndexWhenClassNameIsMissing()
    {
        var engine = CreateEngine();
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;
        var frame = 0;

        Enqueue(engine, ref frame, Detection(2, string.Empty));
        Enqueue(engine, ref frame, Detection(2, string.Empty));
        Enqueue(engine, ref frame, Detection(2, string.Empty));

        Assert.Equal(1, results[^1].Step);
        Assert.Equal("sop1:inspection_connector_ready", results[^1].CurrentStateCode);
    }

    [Fact]
    public void Analyze_IgnoresFramesFromOtherTask()
    {
        var engine = CreateEngine();
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;
        var frame = 0;

        Enqueue(engine, ref frame, "sop2-task", Detection(2, "inspection_connector"));
        Enqueue(engine, ref frame, "sop2-task", Detection(2, "inspection_connector"));
        Enqueue(engine, ref frame, "sop2-task", Detection(2, "inspection_connector"));

        Assert.Null(results[^1].Step);
        Assert.Contains("ignored_task", results[^1].DebugNote);

        CompleteInspectPosition(engine, ref frame);

        Assert.Equal(1, results[^1].Step);
        Assert.Equal("sop1:inspection_connector_ready", results[^1].CurrentStateCode);
    }

    private static void CompleteThroughVisionInspection(AnalysisEngine engine, ref int frame)
    {
        CompleteInspectPosition(engine, ref frame);
        Enqueue(engine, ref frame);
        var bank = GetBank(engine);
        bank.WriteHoldingRegister(11, 1);
        bank.WriteHoldingRegister(12, 1);
        Enqueue(engine, ref frame);
    }

    private static void CompleteThroughTightenNylonTube(AnalysisEngine engine, ref int frame)
    {
        CompleteThroughVisionInspection(engine, ref frame);
        CompleteLubrication(engine, ref frame);
        CompleteTightenNylonTube(engine, ref frame);
    }

    private static void CompleteInspectPosition(AnalysisEngine engine, ref int frame)
    {
        Enqueue(engine, ref frame, Detection(2, "inspection_connector"));
        Enqueue(engine, ref frame, Detection(2, "inspection_connector"));
        Enqueue(engine, ref frame, Detection(2, "inspection_connector"));
    }

    private static void CompleteLubrication(AnalysisEngine engine, ref int frame)
    {
        Enqueue(engine, ref frame, Detection(3, "lubrication"));
        Enqueue(engine, ref frame, Detection(3, "lubrication"));
        Enqueue(engine, ref frame, Detection(3, "lubrication"));
    }

    private static void CompleteTightenNylonTube(AnalysisEngine engine, ref int frame)
    {
        Enqueue(engine, ref frame, Detection(0, "product"));
        Enqueue(engine, ref frame, Detection(0, "product"));
        Enqueue(engine, ref frame, Detection(0, "product"));
    }

    private static void CompleteAutoFlaringInsertion(AnalysisEngine engine, ref int frame)
    {
        Enqueue(engine, ref frame, Detection(0, "product"), Detection(1, "press"));
        Enqueue(engine, ref frame, Detection(0, "product"), Detection(1, "press"));
        Enqueue(engine, ref frame, Detection(0, "product"), Detection(1, "press"));
        for (var i = 0; i < 5; i++)
        {
            Enqueue(engine, ref frame);
        }
    }

    private static AnalysisEngine CreateEngine(ModbusHoldingRegisterBank? bank = null)
    {
        var registerBank = bank ?? new ModbusHoldingRegisterBank();
        var engine = new AnalysisEngine(CreateConfig(), modbusRegisters: registerBank);
        EngineBanks[engine] = registerBank;
        engine.UpdateFsmDefinitions(new[]
        {
            new FsmStepDefinition { Step = 1, Name = "Move connector to inspection position" },
            new FsmStepDefinition { Step = 2, Name = "Camera inspection" },
            new FsmStepDefinition { Step = 3, Name = "Lubrication" },
            new FsmStepDefinition { Step = 4, Name = "Tighten nylon tube" },
            new FsmStepDefinition { Step = 5, Name = "Auto flaring insertion" }
        });
        return engine;
    }

    private static AnalysisConfig CreateConfig()
    {
        return new AnalysisConfig
        {
            EnableOnlineAnalysis = true,
            Strategy = AnalysisStrategyNames.Sop1,
            FrameWindowSize = 40,
            StateWindowSize = 40,
            SopWindowMs = 2000,
            SopMinScoreQ1000 = 250,
            SopMinVisibleRatioQ1000 = 500,
            HoldFrames = 3,
            SourceTaskId = "sop1-task"
        };
    }

    private static void Enqueue(
        AnalysisEngine engine,
        ref int frameIndex,
        params DetectionEntity[] detections)
    {
        Enqueue(engine, ref frameIndex, "sop1-task", 100, detections);
    }

    private static void Enqueue(
        AnalysisEngine engine,
        ref int frameIndex,
        long ptsIncrementMs,
        params DetectionEntity[] detections)
    {
        Enqueue(engine, ref frameIndex, "sop1-task", ptsIncrementMs, detections);
    }

    private static void Enqueue(
        AnalysisEngine engine,
        ref int frameIndex,
        string taskId,
        params DetectionEntity[] detections)
    {
        Enqueue(engine, ref frameIndex, taskId, 100, detections);
    }

    private static void Enqueue(
        AnalysisEngine engine,
        ref int frameIndex,
        string taskId,
        long ptsIncrementMs,
        params DetectionEntity[] detections)
    {
        var index = frameIndex++;
        engine.TryEnqueue(new FrameDetections(
            new FrameEntity
            {
                SourceId = "camera:1",
                SourceType = "camera",
                RunUuid = "run-1",
                RunStartedUtcMs = 1,
                FrameIndex = index,
                TimestampMs = index * 100 + Math.Max(0, ptsIncrementMs - 100),
                FrameUtcMs = index * 100 + Math.Max(0, ptsIncrementMs - 100),
                Width = 640,
                Height = 480
            },
            detections,
            taskId,
            VisionTaskKind.Detection));
    }

    private static DetectionEntity Detection(int classId, string className, float score = 0.9f)
    {
        return Detection(classId, className, score, 10, 10, 100, 100);
    }

    private static DetectionEntity Detection(
        int classId,
        string className,
        float score,
        float x1,
        float y1,
        float x2,
        float y2)
    {
        return new DetectionEntity
        {
            ClassId = classId,
            ClassName = className,
            Score = score,
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2
        };
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<AnalysisEngine, ModbusHoldingRegisterBank> EngineBanks = new();

    private static ModbusHoldingRegisterBank GetBank(AnalysisEngine engine)
    {
        return EngineBanks[engine];
    }
}
