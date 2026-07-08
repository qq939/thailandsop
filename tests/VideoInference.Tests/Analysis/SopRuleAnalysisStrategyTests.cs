namespace VideoInferenceDemo.Tests.Analysis;

public sealed class SopRuleAnalysisStrategyTests
{
    [Fact]
    public void Analyze_MatchesStepByExpectedClassName_FromRecentWindow()
    {
        var config = CreateConfig();
        var engine = new AnalysisEngine(config);
        engine.UpdateFsmDefinitions(new[]
        {
            new FsmStepDefinition { Step = 1, Name = "放入内盒", ExpectedStateCode = "内盒" },
            new FsmStepDefinition { Step = 2, Name = "放入产品", ExpectedStateCode = "产品" }
        });

        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;

        engine.TryEnqueue(CreateFrame(0, 0, Detection(0, "内盒")));
        engine.TryEnqueue(CreateFrame(1, 100, Detection(0, "内盒"), Detection(2, "产品")));

        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].Step);
        Assert.Equal("内盒", results[0].ExpectedStateCode);
        Assert.True(results[0].TransitionOk);
        Assert.Equal(2, results[1].Step);
        Assert.Equal("产品", results[1].ExpectedStateCode);
        Assert.True(results[1].TransitionOk);
    }

    [Fact]
    public void Analyze_UsesStepOrderAsDefaultClassSequence_WhenExpectedStateIsEmpty()
    {
        var config = CreateConfig();
        var engine = new AnalysisEngine(config);
        engine.UpdateFsmDefinitions(new[]
        {
            new FsmStepDefinition { Step = 1, Name = "Step 1" },
            new FsmStepDefinition { Step = 2, Name = "Step 2" }
        });

        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;

        engine.TryEnqueue(CreateFrame(0, 0, Detection(0, "内盒")));
        engine.TryEnqueue(CreateFrame(1, 100, Detection(0, "内盒"), Detection(1, "圆片")));

        Assert.Equal(1, results[0].Step);
        Assert.Equal("class:0", results[0].ExpectedStateCode);
        Assert.Equal(2, results[1].Step);
        Assert.Equal("class:1", results[1].ExpectedStateCode);
    }

    [Fact]
    public void Analyze_InfersExpectedStateFromStepName_WhenExpectedStateIsEmpty()
    {
        var config = CreateConfig();
        var engine = new AnalysisEngine(config);
        engine.UpdateFsmDefinitions(new[]
        {
            new FsmStepDefinition { Step = 1, Name = "内盒到位" },
            new FsmStepDefinition { Step = 2, Name = "产品入内盒" }
        });

        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;

        engine.TryEnqueue(CreateFrame(0, 0, Detection(0, "内盒", 0.9f, 0, 0, 200, 200)));
        engine.TryEnqueue(CreateFrame(1, 100,
            Detection(0, "内盒", 0.9f, 0, 0, 200, 200),
            Detection(2, "产品", 0.9f, 50, 50, 150, 150)));

        Assert.Equal(1, results[0].Step);
        Assert.Equal("inner_box_visible", results[0].ExpectedStateCode);
        Assert.Equal("inner_box_visible", results[0].CurrentStateCode);
        Assert.Equal(2, results[1].Step);
        Assert.Equal("product_in_inner_box", results[1].ExpectedStateCode);
        Assert.Equal("product_in_inner_box", results[1].CurrentStateCode);
    }

    [Fact]
    public void Analyze_FiltersTransientDetection_ByVisibleRatio()
    {
        var config = CreateConfig();
        config.SopMinVisibleRatioQ1000 = 600;
        var engine = new AnalysisEngine(config);
        engine.UpdateFsmDefinitions(new[]
        {
            new FsmStepDefinition { Step = 1, Name = "放入内盒", ExpectedStateCode = "内盒" }
        });

        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;

        engine.TryEnqueue(CreateFrame(0, 0, Detection(0, "内盒")));
        engine.TryEnqueue(CreateFrame(1, 100));

        Assert.Equal(1, results[0].Step);
        Assert.Null(results[1].Step);
    }

    [Fact]
    public void Analyze_IgnoresFramesFromOtherTasks_WhenSourceTaskIdIsConfigured()
    {
        var config = CreateConfig();
        config.SourceTaskId = "sop-task";
        var engine = new AnalysisEngine(config);
        engine.UpdateFsmDefinitions(new[]
        {
            new FsmStepDefinition { Step = 1, Name = "放入内盒", ExpectedStateCode = "内盒" }
        });

        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;

        engine.TryEnqueue(CreateFrame(0, 0, "other-task", Detection(0, "内盒")));
        engine.TryEnqueue(CreateFrame(1, 100, "sop-task", Detection(0, "内盒")));

        Assert.Null(results[0].Step);
        Assert.Contains("ignored_task", results[0].DebugNote);
        Assert.Equal(1, results[1].Step);
    }

    [Fact]
    public void Analyze_MatchesProjectRule_ForProductInsideInnerBox()
    {
        var config = CreateConfig();
        var engine = new AnalysisEngine(config);
        engine.UpdateFsmDefinitions(new[]
        {
            new FsmStepDefinition { Step = 1, Name = "产品入内盒", ExpectedStateCode = "product_in_inner_box" }
        });

        var results = new List<AnalysisResult>();
        engine.ResultReady += results.Add;

        engine.TryEnqueue(CreateFrame(
            0,
            0,
            Detection(0, "内盒", 0.9f, 0, 0, 200, 200),
            Detection(2, "产品", 0.9f, 50, 50, 150, 150)));

        Assert.Equal(1, results[0].Step);
        Assert.Equal("product_in_inner_box", results[0].ExpectedStateCode);
        Assert.Equal("product_in_inner_box", results[0].CurrentStateCode);
    }

    private static AnalysisConfig CreateConfig()
    {
        return new AnalysisConfig
        {
            EnableOnlineAnalysis = true,
            Strategy = AnalysisStrategyNames.SopRules,
            FrameWindowSize = 10,
            StateWindowSize = 10,
            SopWindowMs = 500,
            SopMinScoreQ1000 = 250,
            SopMinVisibleRatioQ1000 = 500
        };
    }

    private static FrameDetections CreateFrame(int frameIndex, long ptsMs, params DetectionEntity[] detections)
    {
        return CreateFrame(frameIndex, ptsMs, "sop-task", detections);
    }

    private static FrameDetections CreateFrame(int frameIndex, long ptsMs, string taskId, params DetectionEntity[] detections)
    {
        return new FrameDetections(
            new FrameEntity
            {
                SourceId = "camera:1",
                SourceType = "camera",
                RunUuid = "run-1",
                RunStartedUtcMs = 1,
                FrameIndex = frameIndex,
                TimestampMs = ptsMs,
                FrameUtcMs = ptsMs,
                Width = 1920,
                Height = 1080
            },
            detections,
            taskId,
            VisionTaskKind.Detection);
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
