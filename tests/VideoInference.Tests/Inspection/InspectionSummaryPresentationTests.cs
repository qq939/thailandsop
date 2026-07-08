using System.Windows.Media;
using VideoInferenceDemo.ImageInspection;

namespace VideoInferenceDemo.Tests.Inspection;

public sealed class InspectionSummaryPresentationTests
{
    [Fact]
    public void BuildInspectionSummary_UnetNg_UsesComponentTextLines()
    {
        var result = new InspectionCycleResult
        {
            RecipeKey = new InspectionRecipeKey("A100", "appearance", "P01"),
            Decision = InspectionCycleDecision.Ng,
            SummaryMessage = "Recipe 'A100/appearance/P01' resolved. Action=roi-inspection; Camera=Cam1; ROI=1; Models=1. ROI\u8017\u65f6: x",
            RoiResults =
            [
                new InspectionRoiResult
                {
                    RoiId = "roi-1",
                    RoiName = "scratch-roi",
                    Decision = InspectionCycleDecision.Ng,
                    DefectComponentCount = 2,
                    DefectSummaryText = "threshold=0.6; accepted=2; raw=5; decision=NG",
                    DefectComponentsText = "#1 area=128 perimeter=66.4 ratio=1.93 bbox=(12,30,41,9) meanProb=0.71 maxProb=0.93\r\n#2 area=64 perimeter=40.0 ratio=1.6 bbox=(50,60,8,8) meanProb=0.66 maxProb=0.88"
                }
            ]
        };

        var summary = VideoInferenceDemo.ImageInspection.MainViewModel.BuildInspectionSummary(result);

        Assert.Contains("#1 \u9762\u79ef=128 \u5468\u957f=66.4 \u9762\u79ef\u5468\u957f\u6bd4=1.93", summary);
        Assert.Contains("#2 \u9762\u79ef=64", summary);
        Assert.Contains("\u6982\u7387=0.71", summary);
        Assert.DoesNotContain("bbox=", summary);
        Assert.DoesNotContain("maxProb=", summary);
        Assert.DoesNotContain("area=", summary);
        Assert.DoesNotContain("perimeter=", summary);
        Assert.DoesNotContain("ratio=", summary);
        Assert.DoesNotContain("\u6700\u5927\u6982\u7387", summary);
        Assert.DoesNotContain("Action=", summary);
        Assert.DoesNotContain("Camera=", summary);
        Assert.DoesNotContain("ROI\u8017\u65f6", summary);
    }

    [Fact]
    public void BuildInspectionSummary_UnetOk_UsesDefectSummaryWhenNoComponents()
    {
        var result = new InspectionCycleResult
        {
            RecipeKey = new InspectionRecipeKey("A100", "appearance", "P01"),
            Decision = InspectionCycleDecision.Ok,
            RoiResults =
            [
                new InspectionRoiResult
                {
                    RoiId = "roi-1",
                    RoiName = "scratch-roi",
                    Decision = InspectionCycleDecision.Ok,
                    DefectComponentCount = 0,
                    DefectSummaryText = "threshold=0.6; accepted=0; raw=0; decision=OK"
                }
            ]
        };

        var summary = VideoInferenceDemo.ImageInspection.MainViewModel.BuildInspectionSummary(result);

        Assert.Equal("threshold=0.6; accepted=0; raw=0; decision=OK", summary);
    }

    [Fact]
    public void BuildInspectionSummary_ProductPresenceSkipped_UsesPresenceSummary()
    {
        var result = new InspectionCycleResult
        {
            RecipeKey = new InspectionRecipeKey("A100", "appearance", "P01"),
            Decision = InspectionCycleDecision.Ng,
            SummaryMessage = "产品有无：无产品，概率=0.502，模型=classifier-shufflenet，已跳过划痕检测",
            Metadata = new Dictionary<string, string>
            {
                ["presence.enabled"] = "true",
                ["presence.status"] = "absent",
                ["presence.skipped"] = "true"
            }
        };

        var summary = VideoInferenceDemo.ImageInspection.MainViewModel.BuildInspectionSummary(result);

        Assert.Equal("产品有无：无产品，概率=0.502，模型=classifier-shufflenet，已跳过划痕检测", summary);
        Assert.DoesNotContain("检测 NG", summary);
    }

    [Fact]
    public void BuildInspectionSummary_ProductPresentPrefixesUnetSummary()
    {
        var result = new InspectionCycleResult
        {
            RecipeKey = new InspectionRecipeKey("A100", "appearance", "P01"),
            Decision = InspectionCycleDecision.Ok,
            SummaryMessage = "产品有无：有产品，概率=0.8，模型=classifier-shufflenet",
            Metadata = new Dictionary<string, string>
            {
                ["presence.enabled"] = "true",
                ["presence.status"] = "present",
                ["presence.skipped"] = "false"
            },
            RoiResults =
            [
                new InspectionRoiResult
                {
                    RoiId = "roi-1",
                    RoiName = "scratch-roi",
                    Decision = InspectionCycleDecision.Ok,
                    DefectComponentCount = 0,
                    DefectSummaryText = "threshold=0.6; accepted=0; raw=0; decision=OK"
                }
            ]
        };

        var summary = VideoInferenceDemo.ImageInspection.MainViewModel.BuildInspectionSummary(result);

        Assert.StartsWith("产品有无：有产品", summary);
        Assert.Contains("threshold=0.6; accepted=0", summary);
    }

    [Fact]
    public void TaskSession_MultiCameraSummaryAccumulatesByCameraAndAggregatesNg()
    {
        var task = CreateTask();
        var cameras = AddCameras(task, 4);
        var triggerId = "trigger-1";

        task.ApplyCameraInspectionResult(
            cameras[0],
            CreateResult(triggerId, cameras[0].Id, InspectionCycleDecision.Ok),
            "cam1-ok-detail");
        var unetResult = CreateUnetResult(triggerId, cameras[1].Id);
        task.ApplyCameraInspectionResult(
            cameras[1],
            unetResult,
            VideoInferenceDemo.ImageInspection.MainViewModel.BuildInspectionSummary(unetResult));
        task.ApplyCameraInspectionResult(
            cameras[2],
            CreateResult(triggerId, cameras[2].Id, InspectionCycleDecision.Ok),
            "cam3-ok-detail");
        task.ApplyCameraInspectionResult(
            cameras[3],
            CreateResult(triggerId, cameras[3].Id, InspectionCycleDecision.Ok),
            "cam4-ok-detail");

        Assert.Contains("\u6c47\u603b\uff1aNG\uff08\u76f8\u673a=4\uff0cOK=3\uff0cNG=1\uff0c\u5904\u7406\u4e2d=0\uff09", task.SummaryMessage);
        Assert.Contains("Cam 1 - OK", task.SummaryMessage);
        Assert.Contains("cam1-ok-detail", task.SummaryMessage);
        Assert.Contains("Cam 2 - NG", task.SummaryMessage);
        Assert.Contains("#1 \u9762\u79ef=128 \u5468\u957f=66.4 \u9762\u79ef\u5468\u957f\u6bd4=1.93 \u6982\u7387=0.71", task.SummaryMessage);
        Assert.Contains("Cam 3 - OK", task.SummaryMessage);
        Assert.Contains("cam3-ok-detail", task.SummaryMessage);
        Assert.Contains("Cam 4 - OK", task.SummaryMessage);
        Assert.Contains("cam4-ok-detail", task.SummaryMessage);
        Assert.DoesNotContain("bbox=", task.SummaryMessage);
        Assert.DoesNotContain("maxProb=", task.SummaryMessage);
        Assert.Equal("NG", task.ResultText);
        AssertBrush(task.ResultBackground, 0xC4, 0x2B, 0x2B);
    }

    [Fact]
    public void TaskSession_MultiCameraSummaryShowsPendingUntilAllCamerasReturn()
    {
        var task = CreateTask();
        var cameras = AddCameras(task, 4);
        var triggerId = "trigger-1";

        task.ApplyCameraInspectionResult(
            cameras[0],
            CreateResult(triggerId, cameras[0].Id, InspectionCycleDecision.Ok),
            "cam1-ok-detail");
        task.ApplyCameraInspectionResult(
            cameras[1],
            CreateResult(triggerId, cameras[1].Id, InspectionCycleDecision.Ok),
            "cam2-ok-detail");

        Assert.Contains("\u6c47\u603b\uff1a\u5904\u7406\u4e2d\uff08\u76f8\u673a=4\uff0cOK=2\uff0cNG=0\uff0c\u5904\u7406\u4e2d=2\uff09", task.SummaryMessage);
        Assert.Contains("Cam 1 - OK", task.SummaryMessage);
        Assert.Contains("Cam 2 - OK", task.SummaryMessage);
        Assert.Contains("Cam 3 - \u5904\u7406\u4e2d", task.SummaryMessage);
        Assert.Contains("Cam 4 - \u5904\u7406\u4e2d", task.SummaryMessage);
        Assert.Equal("\u5904\u7406\u4e2d", task.ResultText);
        AssertBrush(task.ResultBackground, 0x1F, 0x6F, 0xB2);
    }

    [Fact]
    public void TaskSession_MultiCameraSummaryClearsWhenTriggerChanges()
    {
        var task = CreateTask();
        var cameras = AddCameras(task, 4);

        task.ApplyCameraInspectionResult(
            cameras[0],
            CreateResult("trigger-a", cameras[0].Id, InspectionCycleDecision.Ok),
            "old-cam1-detail");
        task.ApplyCameraInspectionResult(
            cameras[1],
            CreateResult("trigger-a", cameras[1].Id, InspectionCycleDecision.Ok),
            "old-cam2-detail");

        task.ApplyCameraInspectionResult(
            cameras[2],
            CreateResult("trigger-b", cameras[2].Id, InspectionCycleDecision.Ng),
            "new-cam3-ng-detail");

        Assert.DoesNotContain("old-cam1-detail", task.SummaryMessage);
        Assert.DoesNotContain("old-cam2-detail", task.SummaryMessage);
        Assert.Contains("Cam 1 - \u5904\u7406\u4e2d", task.SummaryMessage);
        Assert.Contains("Cam 2 - \u5904\u7406\u4e2d", task.SummaryMessage);
        Assert.Contains("Cam 3 - NG", task.SummaryMessage);
        Assert.Contains("new-cam3-ng-detail", task.SummaryMessage);
        Assert.Equal("NG", task.ResultText);
    }

    [Fact]
    public void TaskSession_SingleImageSummaryDoesNotListOtherTaskCameras()
    {
        var task = CreateTask();
        var cameras = AddCameras(task, 4);

        task.ApplyCameraInspectionResult(
            cameras[1],
            CreateResult("image-1", cameras[1].Id, InspectionCycleDecision.Ok),
            "single-image-detail",
            includeAllTaskCameras: false);

        Assert.Equal("single-image-detail", task.SummaryMessage);
        Assert.DoesNotContain("Cam 1 -", task.SummaryMessage);
        Assert.DoesNotContain("Cam 3 -", task.SummaryMessage);
        Assert.Equal("OK", task.ResultText);
    }

    [Fact]
    public void TaskSession_AppliesDecisionPresentationColors()
    {
        var task = CreateTask();

        task.ApplyDecision(InspectionCycleDecision.Ok);
        Assert.Equal("OK", task.ResultText);
        AssertBrush(task.ResultBackground, 0x12, 0x8A, 0x55);

        task.ApplyDecision(InspectionCycleDecision.Ng);
        Assert.Equal("NG", task.ResultText);
        AssertBrush(task.ResultBackground, 0xC4, 0x2B, 0x2B);

        task.ApplyDecision(InspectionCycleDecision.Unknown);
        Assert.Equal("\u672a\u68c0\u6d4b", task.ResultText);
        AssertBrush(task.ResultBackground, 0xE9, 0xEF, 0xF5);
    }

    private static InspectionTaskSessionViewModel CreateTask()
    {
        return new InspectionTaskSessionViewModel(
            "task-instance-1",
            "Task 1",
            "appearance-check",
            "model-a",
            "p01",
            "station-1",
            InspectionActionTypes.RoiInspection,
            "appearance-check");
    }

    private static InspectionCameraSessionViewModel[] AddCameras(
        InspectionTaskSessionViewModel task,
        int count)
    {
        var cameras = Enumerable
            .Range(1, count)
            .Select(index => CreateCamera($"cam-{index}", $"Cam {index}", index))
            .ToArray();
        foreach (var camera in cameras)
        {
            task.Cameras.Add(camera);
        }

        return cameras;
    }

    private static InspectionCameraSessionViewModel CreateCamera(string id, string name, int ordinal)
    {
        return new InspectionCameraSessionViewModel(
            new InspectionCameraProfile
            {
                Id = id,
                Name = name,
                TriggerMode = CameraTriggerMode.Software
            },
            ordinal);
    }

    private static InspectionCycleResult CreateResult(
        string triggerId,
        string cameraId,
        InspectionCycleDecision decision)
    {
        return new InspectionCycleResult
        {
            RecipeKey = new InspectionRecipeKey("A100", "appearance", "P01"),
            CameraId = cameraId,
            TriggerId = triggerId,
            TriggerTime = DateTimeOffset.Parse("2026-06-14T10:00:00+08:00"),
            Decision = decision
        };
    }

    private static InspectionCycleResult CreateUnetResult(string triggerId, string cameraId)
    {
        return new InspectionCycleResult
        {
            RecipeKey = new InspectionRecipeKey("A100", "appearance", "P01"),
            CameraId = cameraId,
            TriggerId = triggerId,
            TriggerTime = DateTimeOffset.Parse("2026-06-14T10:00:00+08:00"),
            Decision = InspectionCycleDecision.Ng,
            RoiResults =
            [
                new InspectionRoiResult
                {
                    RoiId = "roi-1",
                    RoiName = "scratch-roi",
                    Decision = InspectionCycleDecision.Ng,
                    DefectComponentCount = 1,
                    DefectSummaryText = "threshold=0.6; accepted=1; raw=1; decision=NG",
                    DefectComponentsText = "#1 area=128 perimeter=66.4 ratio=1.93 bbox=(12,30,41,9) meanProb=0.71 maxProb=0.93"
                }
            ]
        };
    }

    private static void AssertBrush(Brush brush, byte r, byte g, byte b)
    {
        var solid = Assert.IsType<SolidColorBrush>(brush);
        Assert.Equal(Color.FromRgb(r, g, b), solid.Color);
    }
}
