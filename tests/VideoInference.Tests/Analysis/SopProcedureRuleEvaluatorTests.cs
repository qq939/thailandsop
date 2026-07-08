namespace VideoInferenceDemo.Tests.Analysis;

public sealed class SopProcedureRuleEvaluatorTests
{
    [Fact]
    public void Analyze_CompletesCycle_WhenWarrantyCardIsLastConfiguredStep()
    {
        var engine = CreateEngineWithFinalWarrantyCardStep();
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;

        var frameIndex = 0;
        var ptsMs = 0L;

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox()));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ChargerTopRight()));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), FootPadLeftHalf()));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ProductInside()));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), WarrantyCardInside()));
        }

        var completed = results.Last(item => item.Step == 5);
        Assert.True(completed.IsSopCycleReset);
        Assert.True(completed.IsReset);
        Assert.Contains("cycle_complete", completed.DebugNote);

        engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox()));

        Assert.Equal(1, results[^1].Step);
        Assert.False(results[^1].IsSopCycleReset);
    }

    [Fact]
    public void Analyze_AdvancesThroughConfiguredProcedureSteps()
    {
        var engine = CreateEngine(includeCompleteStep: false);
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;

        var frameIndex = 0;
        var ptsMs = 0L;

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox()));
        }

        Assert.Equal(1, results[^1].Step);
        Assert.Equal("inner_box_ready", results[^1].CurrentStateCode);

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ChargerTopRight()));
        }

        Assert.Equal(2, results[^1].Step);
        Assert.Equal("charger_loaded", results[^1].CurrentStateCode);

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), FootPadLeftHalf()));
        }

        Assert.Equal(3, results[^1].Step);
        Assert.Equal("foot_pad_loaded", results[^1].CurrentStateCode);

        for (var i = 0; i < 5; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ProductInside()));
        }

        Assert.Equal(4, results[^1].Step);
        Assert.Equal("product_loaded", results[^1].CurrentStateCode);

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), WarrantyCardInside()));
        }

        Assert.Equal(5, results[^1].Step);
        Assert.Equal("warranty_card_loaded", results[^1].CurrentStateCode);

        for (var i = 0; i < 5; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, OuterBoxStable()));
        }

        Assert.Equal(6, results[^1].Step);
        Assert.Equal("outer_box_loaded", results[^1].CurrentStateCode);
    }

    [Fact]
    public void Analyze_SupportsSevenStepConfiguration_WithSeparateCompleteStep()
    {
        var engine = CreateEngine(includeCompleteStep: true);
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;

        var frameIndex = 0;
        var ptsMs = 0L;

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox()));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ChargerTopRight()));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), FootPadLeftHalf()));
        }

        for (var i = 0; i < 5; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ProductInside()));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), WarrantyCardInside()));
        }

        for (var i = 0; i < 5; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, OuterBoxStable()));
        }

        Assert.Equal(6, results[^1].Step);
        Assert.Equal("outer_box_loaded", results[^1].CurrentStateCode);

        for (var i = 0; i < 5; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, OuterBoxStable()));
        }

        Assert.Equal(7, results[^1].Step);
        Assert.Equal("sop_complete", results[^1].CurrentStateCode);
    }

    [Fact]
    public void Analyze_CompletesFinalStep_WhenOuterBoxRemainsVisible()
    {
        var engine = CreateEngine(includeCompleteStep: true);
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;

        var frameIndex = 0;
        var ptsMs = 0L;

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox()));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ChargerTopRight()));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), FootPadLeftHalf()));
        }

        for (var i = 0; i < 5; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ProductInside()));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), WarrantyCardInside()));
        }

        for (var i = 0; i < 5; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, OuterBoxStable()));
        }

        Assert.Equal(6, results[^1].Step);

        engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, OuterBoxStable()));

        Assert.Equal(7, results[^1].Step);
        Assert.Equal("sop_complete", results[^1].CurrentStateCode);
    }

    [Fact]
    public void Analyze_RequiresOnlyStableCenteredOuterBox_ForFinalStep()
    {
        var engine = CreateEngine(includeCompleteStep: false);
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;

        var frameIndex = 0;
        var ptsMs = 0L;

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox()));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ChargerTopRight()));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), FootPadLeftHalf()));
        }

        for (var i = 0; i < 5; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ProductInside()));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), WarrantyCardInside()));
        }

        for (var i = 0; i < 5; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, OuterBoxLeft()));
        }

        Assert.Equal(5, results[^1].Step);
        Assert.Equal("outer_box_loaded", results[^1].ExpectedStateCode);
        Assert.Null(results[^1].CurrentStateCode);

        for (var i = 0; i < 5; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, OuterBoxStable(), ProductOutsideInnerBox()));
        }

        Assert.Equal(5, results[^1].Step);
        Assert.Null(results[^1].CurrentStateCode);

        for (var i = 0; i < 5; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, OuterBoxStable()));
        }

        Assert.Equal(6, results[^1].Step);
        Assert.Equal("outer_box_loaded", results[^1].CurrentStateCode);
    }

    [Fact]
    public void Analyze_DoesNotAdvanceToLaterSteps_WhenCurrentExpectedStepIsNotMatched()
    {
        var engine = CreateEngine(includeCompleteStep: true);
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;

        var frameIndex = 0;
        var ptsMs = 0L;

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox()));
        }

        Assert.Equal(1, results[^1].Step);

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(
                frameIndex++,
                ptsMs += 100,
                InnerBox(),
                FootPadLeftHalf(),
                ProductInside(),
                OuterBoxStable()));
        }

        Assert.Equal(1, results[^1].Step);
        Assert.Equal("charger_loaded", results[^1].ExpectedStateCode);
        Assert.Equal("ng:adapter_wrong_object", results[^1].CurrentStateCode);
        Assert.Contains("装适配器步骤", results[^1].NgReason);
        Assert.DoesNotContain(results, item => item.Step == 3);
    }

    [Fact]
    public void Analyze_PrefersClassNameMatching_WhenClassIdsChange()
    {
        var engine = CreateEngine(includeCompleteStep: false);
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;

        var frameIndex = 0;
        var ptsMs = 0L;

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, Detection(0, "内盒", 300, 500, 900, 1000)));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(
                frameIndex++,
                ptsMs += 100,
                Detection(0, "内盒", 300, 500, 900, 1000),
                Detection(99, "充电器", 730, 520, 840, 650)));
        }

        Assert.Equal(2, results[^1].Step);
        Assert.Equal("charger_loaded", results[^1].CurrentStateCode);
    }

    [Fact]
    public void Analyze_RecognizesStepNamesWithCompletedSuffix_AsProcedureActions()
    {
        var engine = CreateEngine(includeCompleteStep: true, useCompletedSuffix: true);
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;

        var frameIndex = 0;
        var ptsMs = 0L;

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox()));
        }

        Assert.Equal(1, results[^1].Step);
        Assert.Equal("inner_box_ready", results[^1].CurrentStateCode);

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ChargerTopRight()));
        }

        Assert.Equal(2, results[^1].Step);
        Assert.Equal("charger_loaded", results[^1].CurrentStateCode);
    }

    [Fact]
    public void Analyze_RaisesNgReason_WhenWrongObjectAppearsDuringAdapterStep()
    {
        var engine = CreateEngine(includeCompleteStep: false);
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;

        var frameIndex = 0;
        var ptsMs = 0L;

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox()));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ProductInside()));
        }

        Assert.Equal(1, results[^1].Step);
        Assert.Equal("charger_loaded", results[^1].ExpectedStateCode);
        Assert.Equal("ng:adapter_wrong_object", results[^1].CurrentStateCode);
        Assert.Contains("装适配器步骤", results[^1].NgReason);
    }

    [Fact]
    public void Analyze_LocksProcedureAfterNg_UntilAnalysisIsReset()
    {
        var engine = CreateEngine(includeCompleteStep: false);
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;

        var frameIndex = 0;
        var ptsMs = 0L;

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox()));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ProductInside()));
        }

        Assert.Equal("ng:adapter_wrong_object", results[^1].CurrentStateCode);

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ChargerTopRight()));
        }

        Assert.Equal(1, results[^1].Step);
        Assert.Equal("ng:adapter_wrong_object", results[^1].CurrentStateCode);

        engine.ResetAnalysis();

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox()));
        }

        Assert.Equal(1, results[^1].Step);
        Assert.Equal("inner_box_ready", results[^1].CurrentStateCode);
        Assert.Null(results[^1].NgReason);
    }

    [Fact]
    public void Analyze_RaisesNgReason_WhenWarrantyCardIsMissingAndInnerBoxDisappears()
    {
        var engine = CreateEngine(includeCompleteStep: false);
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;

        var frameIndex = 0;
        var ptsMs = 0L;

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox()));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ChargerTopRight()));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), FootPadLeftHalf()));
        }

        for (var i = 0; i < 5; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ProductInside()));
        }

        for (var i = 0; i < 5; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, ProductInside()));
        }

        Assert.Equal(4, results[^1].Step);
        Assert.Equal("warranty_card_loaded", results[^1].ExpectedStateCode);
        Assert.Equal("ng:warranty_card_missing", results[^1].CurrentStateCode);
        Assert.Contains("未检测到保修卡", results[^1].NgReason);
    }

    [Fact]
    public void Analyze_DoesNotCountWarrantyCardOutsideInnerBox()
    {
        var engine = CreateEngine(includeCompleteStep: false);
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;

        var frameIndex = 0;
        var ptsMs = 0L;

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox()));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ChargerTopRight()));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ChargerTopRight(), FootPadLeftHalf()));
        }

        for (var i = 0; i < 5; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ChargerTopRight(), FootPadLeftHalf(), ProductInside()));
        }

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ChargerTopRight(), FootPadLeftHalf(), ProductInside(), WarrantyCard()));
        }

        Assert.Equal(4, results[^1].Step);
        Assert.Equal("warranty_card_loaded", results[^1].ExpectedStateCode);
        Assert.Null(results[^1].CurrentStateCode);
    }

    [Fact]
    public void Analyze_DoesNotRaiseAdapterNgForObjectsOutsideInnerBox()
    {
        var engine = CreateEngine(includeCompleteStep: false);
        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;

        var frameIndex = 0;
        var ptsMs = 0L;

        for (var i = 0; i < 3; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox()));
        }

        for (var i = 0; i < 5; i++)
        {
            engine.TryEnqueue(CreateFrame(frameIndex++, ptsMs += 100, InnerBox(), ProductOutsideInnerBox()));
        }

        Assert.Equal(1, results[^1].Step);
        Assert.Equal("charger_loaded", results[^1].ExpectedStateCode);
        Assert.Null(results[^1].CurrentStateCode);
        Assert.Null(results[^1].NgReason);
    }

    private static AnalysisEngine CreateEngine(bool includeCompleteStep, bool useCompletedSuffix = false)
    {
        var config = new AnalysisConfig
        {
            EnableOnlineAnalysis = true,
            Strategy = AnalysisStrategyNames.SopRules,
            FrameWindowSize = 60,
            StateWindowSize = 20,
            SopWindowMs = 5000,
            SopMinScoreQ1000 = 250,
            SopMinVisibleRatioQ1000 = 500
        };

        var suffix = useCompletedSuffix ? "完成" : string.Empty;
        var steps = new List<FsmStepDefinition>
        {
            new() { Step = 1, Name = $"拿内盒{suffix}" },
            new() { Step = 2, Name = $"装适配器{suffix}" },
            new() { Step = 3, Name = $"装脚垫{suffix}" },
            new() { Step = 4, Name = $"装产品{suffix}" },
            new() { Step = 5, Name = $"装保修卡{suffix}" },
            new() { Step = 6, Name = includeCompleteStep ? $"装内盒{suffix}" : $"装外盒{suffix}" }
        };

        if (includeCompleteStep)
        {
            steps.Add(new FsmStepDefinition { Step = 7, Name = useCompletedSuffix ? "SOP完成" : "完成" });
        }

        var engine = new AnalysisEngine(config);
        engine.UpdateFsmDefinitions(steps);
        return engine;
    }

    private static AnalysisEngine CreateEngineWithFinalWarrantyCardStep()
    {
        var config = new AnalysisConfig
        {
            EnableOnlineAnalysis = true,
            Strategy = AnalysisStrategyNames.SopRules,
            FrameWindowSize = 60,
            StateWindowSize = 20,
            SopWindowMs = 5000,
            SopMinScoreQ1000 = 250,
            SopMinVisibleRatioQ1000 = 500
        };

        var engine = new AnalysisEngine(config);
        engine.UpdateFsmDefinitions(new List<FsmStepDefinition>
        {
            new() { Step = 1, Name = "拿内盒" },
            new() { Step = 2, Name = "装电源" },
            new() { Step = 3, Name = "装脚垫" },
            new() { Step = 4, Name = "装产品" },
            new() { Step = 5, Name = "装保修卡" }
        });
        return engine;
    }

    private static FrameDetections CreateFrame(int frameIndex, long ptsMs, params DetectionEntity[] detections)
    {
        return new FrameDetections(
            new FrameEntity
            {
                SourceId = "camera:1",
                SourceType = "camera",
                RunUuid = "run-procedure",
                RunStartedUtcMs = 1,
                FrameIndex = frameIndex,
                TimestampMs = ptsMs,
                FrameUtcMs = ptsMs,
                Width = 1920,
                Height = 1080
            },
            detections,
            "sop-task",
            VisionTaskKind.Detection);
    }

    private static DetectionEntity InnerBox()
    {
        return Detection(0, "内盒", 300, 500, 900, 1000);
    }

    private static DetectionEntity ChargerTopRight()
    {
        return Detection(4, "充电器", 730, 520, 840, 650);
    }

    private static DetectionEntity FootPadLeftHalf()
    {
        return Detection(1, "脚垫", 350, 640, 500, 780);
    }

    private static DetectionEntity ProductInside()
    {
        return Detection(2, "产品", 440, 600, 820, 920);
    }

    private static DetectionEntity ProductOutsideInnerBox()
    {
        return Detection(2, "product", 980, 180, 1160, 300);
    }

    private static DetectionEntity WarrantyCardInside()
    {
        return Detection(5, "warranty_card", 520, 700, 700, 820);
    }

    private static DetectionEntity WarrantyCard()
    {
        return Detection(5, "保修卡", 980, 180, 1160, 300);
    }

    private static DetectionEntity OuterBoxStable()
    {
        return Detection(3, "外盒", 600, 80, 1200, 420);
    }

    private static DetectionEntity OuterBoxLeft()
    {
        return Detection(3, "外盒", 80, 80, 760, 420);
    }

    private static DetectionEntity Detection(int classId, string className, float x1, float y1, float x2, float y2)
    {
        return new DetectionEntity
        {
            ClassId = classId,
            ClassName = className,
            Score = 0.9f,
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2
        };
    }
}
