namespace VideoInferenceDemo.Tests.Analysis;

public sealed class Sop2AnalysisStrategyTests
{
    [Fact]
    public void Analyze_CompletesNormalFlowAndResetsForNextCycle()
    {
        var bank = new ModbusHoldingRegisterBank();
        var engine = CreateEngine(bank);
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;
        var frame = 0;

        CompleteTightenNylonTube(engine, ref frame);
        Assert.Equal(1, results[^1].Step);
        Assert.Equal("sop2:nylon_tube_tightened", results[^1].CurrentStateCode);

        CompleteInspectPosition(engine, ref frame);
        Assert.Equal(2, results[^1].Step);
        Assert.Equal("sop2:inspection_connector_ready", results[^1].CurrentStateCode);

        Enqueue(engine, ref frame);
        Assert.Equal((ushort)1, bank.ReadHoldingRegister(0));
        Assert.Equal((ushort)0, bank.ReadHoldingRegister(1));
        Assert.Equal((ushort)0, bank.ReadHoldingRegister(2));

        bank.WriteHoldingRegister(1, 1);
        bank.WriteHoldingRegister(2, 1);
        Enqueue(engine, ref frame);
        Assert.Equal(3, results[^1].Step);
        Assert.Equal("sop2:visual_inspection_ok", results[^1].CurrentStateCode);
        Assert.Equal((ushort)0, bank.ReadHoldingRegister(0));

        for (var i = 0; i < 5; i++)
        {
            Enqueue(engine, ref frame, Detection(4, "expansion"));
        }

        Assert.Equal(3, results[^1].Step);
        Assert.Contains("expansion_seen", results[^1].DebugNote);

        Enqueue(engine, ref frame);
        Enqueue(engine, ref frame);
        Enqueue(engine, ref frame);
        Assert.Equal(4, results[^1].Step);

        for (var i = 0; i < 5; i++)
        {
            Enqueue(engine, ref frame, Detection(8, "lubrication"));
        }

        Assert.Equal(5, results[^1].Step);

        Enqueue(engine, ref frame,
            Detection(3, "fixed_block", 0.9f, 0, 0, 40, 40),
            Detection(2, "press", 0.9f, 200, 0, 240, 40));
        Enqueue(engine, ref frame,
            Detection(3, "fixed_block", 0.9f, 0, 0, 40, 40),
            Detection(2, "press", 0.9f, 100, 0, 140, 40));
        Assert.Equal(5, results[^1].Step);
        Assert.Contains("press_moved_in", results[^1].DebugNote);

        Enqueue(engine, ref frame,
            Detection(3, "fixed_block", 0.9f, 0, 0, 40, 40),
            Detection(2, "press", 0.9f, 200, 0, 240, 40));
        Assert.Equal(6, results[^1].Step);
        Assert.Equal("sop2:press_completed", results[^1].CurrentStateCode);

        Enqueue(engine, ref frame);
        Assert.Null(results[^1].Step);
        Assert.True(results[^1].IsReset);
        Assert.Equal("sop2:product_or_product2_with_handle", results[^1].ExpectedStateCode);
    }

    [Fact]
    public void Analyze_WaitsForProductAndHandleBeforeCompletingFirstStep()
    {
        var engine = CreateEngine();
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;
        var frame = 0;

        Enqueue(engine, ref frame, Detection(5, "handle"));
        Enqueue(engine, ref frame, Detection(5, "handle"));
        Enqueue(engine, ref frame, Detection(5, "handle"));

        Assert.Null(results[^1].Step);
        Assert.Null(results[^1].CurrentStateCode);
        Assert.Equal("sop2:product_or_product2_with_handle", results[^1].ExpectedStateCode);
        Assert.Null(results[^1].NgReason);

        CompleteTightenNylonTube(engine, ref frame);

        Assert.Equal(1, results[^1].Step);
        Assert.Equal("sop2:nylon_tube_tightened", results[^1].CurrentStateCode);
    }

    [Fact]
    public void Analyze_ModbusNgAndErrorValuesFaultAndClearTrigger()
    {
        var ngBank = new ModbusHoldingRegisterBank();
        var ngEngine = CreateEngine(ngBank);
        var ngResults = new List<AnalysisResult>();
        ngEngine.ResultReady += ngResults.Add;
        var ngFrame = 0;
        CompleteThroughInspectPosition(ngEngine, ref ngFrame);
        Enqueue(ngEngine, ref ngFrame);
        ngBank.WriteHoldingRegister(1, 1);
        ngBank.WriteHoldingRegister(2, 2);
        Enqueue(ngEngine, ref ngFrame);

        Assert.Equal("ng:sop2_visual_inspection", ngResults[^1].CurrentStateCode);
        Assert.Equal(3, ngResults[^1].Step);
        Assert.Equal((ushort)0, ngBank.ReadHoldingRegister(0));

        var errorBank = new ModbusHoldingRegisterBank();
        var errorEngine = CreateEngine(errorBank);
        var errorResults = new List<AnalysisResult>();
        errorEngine.ResultReady += errorResults.Add;
        var errorFrame = 0;
        CompleteThroughInspectPosition(errorEngine, ref errorFrame);
        Enqueue(errorEngine, ref errorFrame);
        errorBank.WriteHoldingRegister(1, 1);
        errorBank.WriteHoldingRegister(2, 4);
        Enqueue(errorEngine, ref errorFrame);

        Assert.Equal("ng:sop2_visual_inspection", errorResults[^1].CurrentStateCode);
        Assert.Equal(3, errorResults[^1].Step);
        Assert.Equal((ushort)0, errorBank.ReadHoldingRegister(0));
    }

    [Fact]
    public void Analyze_ModbusTimeoutFaultsAndClearsTrigger()
    {
        var bank = new ModbusHoldingRegisterBank();
        var engine = CreateEngine(bank);
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;
        var frame = 0;

        CompleteThroughInspectPosition(engine, ref frame);
        Enqueue(engine, ref frame);
        Enqueue(engine, ref frame, ptsIncrementMs: 10050);

        Assert.Equal("ng:sop2_visual_inspection_timeout", results[^1].CurrentStateCode);
        Assert.Equal(3, results[^1].Step);
        Assert.Equal((ushort)0, bank.ReadHoldingRegister(0));
    }

    [Fact]
    public void Analyze_MatchesSop2ClassesByIndexWhenClassNameIsMissing()
    {
        var engine = CreateEngine();
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;
        var frame = 0;

        Enqueue(engine, ref frame, Detection(0, string.Empty), Detection(5, string.Empty));
        Enqueue(engine, ref frame, Detection(0, string.Empty), Detection(5, string.Empty));
        Enqueue(engine, ref frame, Detection(1, string.Empty), Detection(5, string.Empty));

        Assert.Equal(1, results[^1].Step);
        Assert.Equal("sop2:nylon_tube_tightened", results[^1].CurrentStateCode);

        Enqueue(engine, ref frame, Detection(7, string.Empty));
        Enqueue(engine, ref frame, Detection(7, string.Empty));
        Enqueue(engine, ref frame, Detection(7, string.Empty));

        Assert.Equal(2, results[^1].Step);
        Assert.Equal("sop2:inspection_connector_ready", results[^1].CurrentStateCode);
    }

    private static void CompleteThroughInspectPosition(AnalysisEngine engine, ref int frame)
    {
        CompleteTightenNylonTube(engine, ref frame);
        CompleteInspectPosition(engine, ref frame);
    }

    private static void CompleteTightenNylonTube(AnalysisEngine engine, ref int frame)
    {
        Enqueue(engine, ref frame, Detection(0, "product"), Detection(5, "handle"));
        Enqueue(engine, ref frame, Detection(0, "product"), Detection(5, "handle"));
        Enqueue(engine, ref frame, Detection(1, "product2"), Detection(5, "handle"));
    }

    private static void CompleteInspectPosition(AnalysisEngine engine, ref int frame)
    {
        Enqueue(engine, ref frame, Detection(7, "inspection_connector"));
        Enqueue(engine, ref frame, Detection(7, "inspection_connector"));
        Enqueue(engine, ref frame, Detection(7, "inspection_connector"));
    }

    private static AnalysisEngine CreateEngine(ModbusHoldingRegisterBank? bank = null)
    {
        var engine = new AnalysisEngine(CreateConfig(), modbusRegisters: bank ?? new ModbusHoldingRegisterBank());
        engine.UpdateFsmDefinitions(new[]
        {
            new FsmStepDefinition { Step = 1, Name = "Tighten nylon tube" },
            new FsmStepDefinition { Step = 2, Name = "Move connector to inspection position" },
            new FsmStepDefinition { Step = 3, Name = "Vision inspection" },
            new FsmStepDefinition { Step = 4, Name = "Expansion" },
            new FsmStepDefinition { Step = 5, Name = "Lubrication" },
            new FsmStepDefinition { Step = 6, Name = "Move connector to pressing mechanism" }
        });
        return engine;
    }

    private static AnalysisConfig CreateConfig()
    {
        return new AnalysisConfig
        {
            EnableOnlineAnalysis = true,
            Strategy = AnalysisStrategyNames.Sop2,
            FrameWindowSize = 40,
            StateWindowSize = 40,
            SopWindowMs = 2000,
            SopMinScoreQ1000 = 250,
            SopMinVisibleRatioQ1000 = 500,
            HoldFrames = 3
        };
    }

    private static void Enqueue(
        AnalysisEngine engine,
        ref int frameIndex,
        params DetectionEntity[] detections)
    {
        Enqueue(engine, ref frameIndex, 100, detections);
    }

    private static void Enqueue(
        AnalysisEngine engine,
        ref int frameIndex,
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
            "sop2-task",
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
}
