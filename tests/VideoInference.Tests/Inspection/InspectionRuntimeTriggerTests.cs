using System.Collections.Concurrent;
using OpenCvSharp;
using VideoInferenceDemo.ImageInspection;
using VideoInferenceDemo.ImageInspection.Runtime;

namespace VideoInferenceDemo.Tests.Inspection;

public sealed class InspectionRuntimeTriggerTests
{
    [Fact]
    public async Task StartTaskAsync_RejectsHardwareLine0CameraInCommandTask()
    {
        var session = new FakeCameraSession("cam-1", CameraTriggerMode.HardwareLine0);
        using var runtime = CreateRuntime([session]);
        var task = CreateTask(InspectionTaskTriggerMode.TriggerCommand, CreateCamera("cam-1", CameraTriggerMode.HardwareLine0));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartTaskAsync(task));

        Assert.Contains("hardware trigger", ex.Message);
        Assert.False(task.IsRunning);
    }

    [Fact]
    public async Task StartTaskAsync_RejectsNonHardwareCameraInCallbackTask()
    {
        var session = new FakeCameraSession("cam-1", CameraTriggerMode.Software);
        using var runtime = CreateRuntime([session]);
        var task = CreateTask(InspectionTaskTriggerMode.CameraCallback, CreateCamera("cam-1", CameraTriggerMode.Software));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartTaskAsync(task));

        Assert.Contains("hardware trigger", ex.Message);
        Assert.False(task.IsRunning);
    }

    [Fact]
    public async Task CameraCallbackTask_WaitsForAllCamerasBeforeExecuting()
    {
        var cam1 = new FakeCameraSession("cam-1", CameraTriggerMode.HardwareLine0);
        var cam2 = new FakeCameraSession("cam-2", CameraTriggerMode.HardwareLine0);
        var action = new RecordingInspectionAction(request =>
            request.CameraId == "cam-2" ? InspectionCycleDecision.Ng : InspectionCycleDecision.Ok);
        using var runtime = CreateRuntime([cam1, cam2], action);
        var task = CreateTask(
            InspectionTaskTriggerMode.CameraCallback,
            CreateCamera("cam-1", CameraTriggerMode.HardwareLine0),
            CreateCamera("cam-2", CameraTriggerMode.HardwareLine0));
        var frames = new List<InspectionRuntimeFrameResult>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.FrameProcessed += frame =>
        {
            lock (frames)
            {
                frames.Add(frame);
                if (frames.Count == 2)
                {
                    completed.TrySetResult();
                }
            }
        };

        await runtime.StartTaskAsync(task);
        cam1.RaiseFrame();
        await Task.Delay(150);

        lock (frames)
        {
            Assert.Empty(frames);
        }

        cam2.RaiseFrame();
        await WaitAsync(completed.Task);

        lock (frames)
        {
            Assert.Equal(["cam-1", "cam-2"], frames.Select(frame => frame.Camera.Id).Order().ToArray());
            Assert.Single(frames.Select(frame => frame.Result.TriggerId).Distinct());
        }

        Assert.Equal(2, action.Requests.Count);
    }

    [Fact]
    public async Task CameraCallbackTask_TimesOutWhenFrameSetIsIncomplete()
    {
        var cam1 = new FakeCameraSession("cam-1", CameraTriggerMode.HardwareLine0);
        var cam2 = new FakeCameraSession("cam-2", CameraTriggerMode.HardwareLine0);
        using var runtime = CreateRuntime([cam1, cam2]);
        var task = CreateTask(
            InspectionTaskTriggerMode.CameraCallback,
            CreateCamera("cam-1", CameraTriggerMode.HardwareLine0),
            CreateCamera("cam-2", CameraTriggerMode.HardwareLine0));
        var failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.TaskFailed += (_, message) => failed.TrySetResult(message);

        await runtime.StartTaskAsync(task);
        cam1.RaiseFrame();

        var message = await WaitAsync(failed.Task, TimeSpan.FromSeconds(3));

        Assert.Contains("timed out", message);
        Assert.True(cam1.ClearCount > 0);
        Assert.True(cam2.ClearCount > 0);
    }

    [Fact]
    public async Task TriggerCommandTask_ReturnsAggregatedDecision()
    {
        var cam1 = new FakeCameraSession("cam-1", CameraTriggerMode.Software);
        var cam2 = new FakeCameraSession("cam-2", CameraTriggerMode.Software);
        var action = new RecordingInspectionAction(request =>
            request.CameraId == "cam-2" ? InspectionCycleDecision.Ng : InspectionCycleDecision.Ok);
        using var runtime = CreateRuntime([cam1, cam2], action);
        var task = CreateTask(
            InspectionTaskTriggerMode.TriggerCommand,
            CreateCamera("cam-1", CameraTriggerMode.Software),
            CreateCamera("cam-2", CameraTriggerMode.Software));
        await runtime.StartTaskAsync(task);

        var triggered = runtime.TryTrigger(task, "unit-test", out var completion);
        var result = await WaitAsync(completion);

        Assert.True(triggered);
        Assert.True(result.Executed);
        Assert.Equal(InspectionCycleDecision.Ng, result.Decision);
        Assert.Equal(1, cam1.TriggerCount);
        Assert.Equal(1, cam2.TriggerCount);
    }

    [Fact]
    public async Task TriggerCommandTask_ProductAbsentSkipsInspectionActionForAllCameras()
    {
        var cam1 = new FakeCameraSession("cam-1", CameraTriggerMode.Software);
        var cam2 = new FakeCameraSession("cam-2", CameraTriggerMode.Software);
        var action = new RecordingInspectionAction(_ => InspectionCycleDecision.Ok);
        var gate = new FakeProductPresenceGate(CreatePresenceResult(absent: true));
        using var runtime = CreateRuntime([cam1, cam2], action, productPresenceGate: gate);
        var task = CreateTask(
            InspectionTaskTriggerMode.TriggerCommand,
            CreateCamera("cam-1", CameraTriggerMode.Software),
            CreateCamera("cam-2", CameraTriggerMode.Software));
        var frames = new List<InspectionRuntimeFrameResult>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.FrameProcessed += frame =>
        {
            lock (frames)
            {
                frames.Add(frame);
                if (frames.Count == 2)
                {
                    completed.TrySetResult();
                }
            }
        };

        await runtime.StartTaskAsync(task);
        Assert.True(runtime.TryTrigger(task, "unit-test", out var completion));
        var result = await WaitAsync(completion);
        await WaitAsync(completed.Task);

        Assert.True(result.Executed);
        Assert.Equal(InspectionCycleDecision.Ng, result.Decision);
        Assert.Empty(action.Requests);
        Assert.Equal(1, gate.EvaluateCount);
        lock (frames)
        {
            Assert.All(frames, frame =>
            {
                Assert.Equal(InspectionCycleDecision.Ng, frame.Result.Decision);
                Assert.Contains("跳过划痕检测", frame.Result.SummaryMessage);
            });
            Assert.Contains(frames, frame => frame.Camera.Id == "cam-1" && frame.Result.Metadata["presence.primaryCameraId"] == "cam-1");
        }
    }

    [Fact]
    public async Task TriggerCommandTask_ProductPresentRunsInspectionActionForAllCameras()
    {
        var cam1 = new FakeCameraSession("cam-1", CameraTriggerMode.Software);
        var cam2 = new FakeCameraSession("cam-2", CameraTriggerMode.Software);
        var action = new RecordingInspectionAction(_ => InspectionCycleDecision.Ok);
        var gate = new FakeProductPresenceGate(CreatePresenceResult(absent: false));
        using var runtime = CreateRuntime([cam1, cam2], action, productPresenceGate: gate);
        var task = CreateTask(
            InspectionTaskTriggerMode.TriggerCommand,
            CreateCamera("cam-1", CameraTriggerMode.Software),
            CreateCamera("cam-2", CameraTriggerMode.Software));
        var frames = new List<InspectionRuntimeFrameResult>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.FrameProcessed += frame =>
        {
            lock (frames)
            {
                frames.Add(frame);
                if (frames.Count == 2)
                {
                    completed.TrySetResult();
                }
            }
        };

        await runtime.StartTaskAsync(task);
        Assert.True(runtime.TryTrigger(task, "unit-test", out var completion));
        var result = await WaitAsync(completion);
        await WaitAsync(completed.Task);

        Assert.True(result.Executed);
        Assert.Equal(InspectionCycleDecision.Ok, result.Decision);
        Assert.Equal(2, action.Requests.Count);
        Assert.Equal(1, gate.EvaluateCount);
        lock (frames)
        {
            var primary = Assert.Single(frames.Where(frame => frame.Camera.Id == "cam-1"));
            Assert.Contains("有产品", primary.Result.SummaryMessage);
            Assert.True(primary.Result.Metadata.ContainsKey("presence.presentProbability"));
            var secondary = Assert.Single(frames.Where(frame => frame.Camera.Id == "cam-2"));
            Assert.False(secondary.Result.Metadata.ContainsKey("presence.presentProbability"));
        }
    }

    [Fact]
    public async Task TriggerCommandTask_AttachesOperatorSnapshot()
    {
        var cam1 = new FakeCameraSession("cam-1", CameraTriggerMode.Software);
        var action = new RecordingInspectionAction(_ => InspectionCycleDecision.Ok);
        using var runtime = CreateRuntime(
            [cam1],
            action,
            () => new InspectionOperatorSnapshot("E001", "Operator A", "A"));
        var task = CreateTask(
            InspectionTaskTriggerMode.TriggerCommand,
            CreateCamera("cam-1", CameraTriggerMode.Software));
        await runtime.StartTaskAsync(task);

        Assert.True(runtime.TryTrigger(task, "unit-test", out var completion));
        await WaitAsync(completion);

        var request = Assert.Single(action.Requests);
        Assert.NotNull(request.Operator);
        Assert.Equal("E001", request.Operator!.EmployeeCode);
        Assert.Equal("Operator A", request.Operator.EmployeeName);
    }

    [Fact]
    public async Task TryTrigger_OnlyRunsRequestedTaskInstance()
    {
        var cam1 = new FakeCameraSession("cam-1", CameraTriggerMode.Software);
        var cam2 = new FakeCameraSession("cam-2", CameraTriggerMode.Software);
        using var runtime = CreateRuntime([cam1, cam2]);
        var task1 = CreateTask(InspectionTaskTriggerMode.TriggerCommand, CreateCamera("cam-1", CameraTriggerMode.Software));
        var task2 = new InspectionTaskSessionViewModel(
            "task-instance-2",
            "Task 2",
            "appearance-check",
            "model-a",
            "p01",
            "station-1",
            InspectionActionTypes.RoiInspection,
            "appearance-check",
            InspectionTaskTriggerMode.TriggerCommand);
        task2.Cameras.Add(CreateCamera("cam-2", CameraTriggerMode.Software));
        await runtime.StartTaskAsync(task1);
        await runtime.StartTaskAsync(task2);

        Assert.True(runtime.TryTrigger(task1, "unit-test", out var completion));
        var result = await WaitAsync(completion);

        Assert.True(result.Executed);
        Assert.Equal(1, cam1.TriggerCount);
        Assert.Equal(0, cam2.TriggerCount);
    }

    private static InspectionRuntimeService CreateRuntime(
        IReadOnlyList<FakeCameraSession> sessions,
        RecordingInspectionAction? action = null,
        Func<InspectionOperatorSnapshot?>? operatorProvider = null,
        IProductPresenceInspectionGate? productPresenceGate = null)
    {
        var registry = new InspectionActionRegistry();
        registry.Register(InspectionActionTypes.RoiInspection, () => action ?? new RecordingInspectionAction(_ => InspectionCycleDecision.Ok));
        return new InspectionRuntimeService(
            new CameraProviderRegistry([new FakeCameraProvider(sessions)]),
            registry,
            operatorProvider,
            productPresenceGate);
    }

    private static InspectionTaskSessionViewModel CreateTask(
        InspectionTaskTriggerMode triggerMode,
        params InspectionCameraSessionViewModel[] cameras)
    {
        var task = new InspectionTaskSessionViewModel(
            "task-instance-1",
            "Task 1",
            "appearance-check",
            "model-a",
            "p01",
            "station-1",
            InspectionActionTypes.RoiInspection,
            "appearance-check",
            triggerMode);
        foreach (var camera in cameras)
        {
            task.Cameras.Add(camera);
        }

        return task;
    }

    private static InspectionCameraSessionViewModel CreateCamera(string id, CameraTriggerMode triggerMode)
    {
        return new InspectionCameraSessionViewModel(
            new InspectionCameraProfile
            {
                Id = id,
                Name = id,
                ProviderId = CameraProviderIds.HikRobot,
                DeviceId = id,
                TriggerMode = triggerMode,
                SaveImages = false
            },
            1);
    }

    private static async Task<T> WaitAsync<T>(Task<T> task, TimeSpan? timeout = null)
    {
        var delay = Task.Delay(timeout ?? TimeSpan.FromSeconds(2));
        var completed = await Task.WhenAny(task, delay);
        Assert.Same(task, completed);
        return await task;
    }

    private static async Task WaitAsync(Task task, TimeSpan? timeout = null)
    {
        var delay = Task.Delay(timeout ?? TimeSpan.FromSeconds(2));
        var completed = await Task.WhenAny(task, delay);
        Assert.Same(task, completed);
        await task;
    }

    private static ProductPresenceInspectionResult CreatePresenceResult(bool absent)
    {
        var metadata = new PresenceClassificationMetadata
        {
            PresentClass = "OK",
            AbsentClass = "NG",
            ProbabilityThreshold = 0.5f
        };
        var result = new PresenceClassificationResult(
            absent
                ?
                [
                    new PresenceClassificationScore(0, "OK", 0.2f, 0f),
                    new PresenceClassificationScore(1, "NG", 0.8f, 1f)
                ]
                :
                [
                    new PresenceClassificationScore(0, "OK", 0.8f, 1f),
                    new PresenceClassificationScore(1, "NG", 0.2f, 0f)
                ],
            metadata);
        return ProductPresenceInspectionResult.Present(
            "presence-model",
            "Presence Model",
            result,
            new Dictionary<string, string>());
    }

    private sealed class FakeProductPresenceGate : IProductPresenceInspectionGate
    {
        private readonly ProductPresenceInspectionResult _result;

        public FakeProductPresenceGate(ProductPresenceInspectionResult result)
        {
            _result = result;
        }

        public int EvaluateCount { get; private set; }

        public ProductPresenceInspectionResult Evaluate(Mat image)
        {
            EvaluateCount++;
            return _result;
        }
    }

    private sealed class RecordingInspectionAction : IInspectionAction
    {
        private readonly Func<InspectionRequest, InspectionCycleDecision> _decide;

        public RecordingInspectionAction(Func<InspectionRequest, InspectionCycleDecision> decide)
        {
            _decide = decide;
        }

        public ConcurrentBag<InspectionRequest> Requests { get; } = [];

        public InspectionCycleResult Execute(InspectionRequest request)
        {
            Requests.Add(request);
            return new InspectionCycleResult
            {
                RecipeKey = request.RecipeKey,
                StationId = request.StationId,
                TaskInstanceId = request.TaskInstanceId,
                CameraId = request.CameraId,
                ActionType = request.ActionType,
                TriggerId = request.TriggerId,
                TriggerTime = request.TriggerTime,
                Decision = _decide(request)
            };
        }
    }

    private sealed class FakeCameraProvider : ICameraProvider
    {
        private readonly IReadOnlyDictionary<string, FakeCameraSession> _sessions;

        public FakeCameraProvider(IEnumerable<FakeCameraSession> sessions)
        {
            _sessions = sessions.ToDictionary(session => session.DeviceId, StringComparer.OrdinalIgnoreCase);
        }

        public string ProviderId => CameraProviderIds.HikRobot;

        public string DisplayName => "Fake Hik";

        public IReadOnlyList<CameraDeviceInfo> EnumerateDevices()
        {
            return _sessions.Keys
                .Select(id => new CameraDeviceInfo(CameraProviderIds.HikRobot, id, id))
                .ToArray();
        }

        public ICameraSession Open(CameraOpenOptions options)
        {
            var id = options.Normalize().DeviceId ?? string.Empty;
            return _sessions[id];
        }
    }

    private sealed class FakeCameraSession : ICameraSession, ICameraFrameCallbackSource
    {
        private readonly ConcurrentQueue<Mat> _frames = new();
        private int _frameNumber;

        public FakeCameraSession(string deviceId, CameraTriggerMode triggerMode)
        {
            DeviceId = deviceId;
            SourceId = $"{CameraProviderIds.HikRobot}:{deviceId}";
            DisplayName = deviceId;
            TriggerMode = triggerMode;
        }

        public string DeviceId { get; }

        public string SourceId { get; }

        public string DisplayName { get; }

        public double ReportedFps => 10;

        public CameraTriggerMode TriggerMode { get; }

        public int TriggerCount { get; private set; }

        public int ClearCount { get; private set; }

        public event EventHandler<CameraFrameArrivedEventArgs>? FrameArrived;

        public bool TryRead(Mat destination, CancellationToken cancellationToken, out CameraFrameMetadata metadata)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
            while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                if (_frames.TryDequeue(out var frame))
                {
                    using (frame)
                    {
                        frame.CopyTo(destination);
                    }

                    metadata = CreateMetadata(_frameNumber);
                    return true;
                }

                Thread.Sleep(10);
            }

            metadata = default!;
            return false;
        }

        public bool TryTrigger(CancellationToken cancellationToken)
        {
            TriggerCount++;
            RaiseFrame();
            return true;
        }

        public void RaiseFrame()
        {
            var frameNumber = Interlocked.Increment(ref _frameNumber);
            _frames.Enqueue(new Mat(8, 8, MatType.CV_8UC3, new Scalar(frameNumber, frameNumber, frameNumber)));
            FrameArrived?.Invoke(this, new CameraFrameArrivedEventArgs(SourceId, DisplayName, CreateMetadata(frameNumber)));
        }

        public void ClearPendingFrames()
        {
            ClearCount++;
            while (_frames.TryDequeue(out var frame))
            {
                frame.Dispose();
            }
        }

        public void Dispose()
        {
            ClearPendingFrames();
        }

        private static CameraFrameMetadata CreateMetadata(int frameNumber)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return new CameraFrameMetadata(now, now, CameraPtsSource.MonotonicClockFallback, FrameNumber: (uint)frameNumber);
        }
    }
}
