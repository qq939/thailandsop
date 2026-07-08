using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace VideoInferenceDemo.ImageInspection.Runtime;

public sealed class InspectionRuntimeService : IDisposable
{
    private static readonly ImageEncodingParam[] JpegEncodingParams =
    [
        new(ImwriteFlags.JpegQuality, 90)
    ];

    private readonly CameraProviderRegistry _cameraProviders;
    private readonly InspectionActionRegistry _actionRegistry;
    private readonly IProductPresenceInspectionGate _productPresenceGate;
    private readonly Func<InspectionOperatorSnapshot?> _operatorProvider;
    private readonly Channel<RuntimeTriggerMessage> _triggers;
    private readonly Channel<CameraFrameArrivalMessage> _cameraFrames;
    private readonly Channel<InspectionImageSaveJob> _imageSaveJobs;
    private readonly ConcurrentDictionary<string, RunningTaskContext> _tasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RunningCameraContext> _cameras = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cameraStartSync = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pumpTask;
    private readonly Task _imageSaveTask;

    public InspectionRuntimeService(
        CameraProviderRegistry cameraProviders,
        InspectionActionRegistry actionRegistry,
        Func<InspectionOperatorSnapshot?>? operatorProvider = null,
        IProductPresenceInspectionGate? productPresenceGate = null)
    {
        _cameraProviders = cameraProviders ?? throw new ArgumentNullException(nameof(cameraProviders));
        _actionRegistry = actionRegistry ?? throw new ArgumentNullException(nameof(actionRegistry));
        _productPresenceGate = productPresenceGate ?? new DisabledProductPresenceInspectionGate();
        _operatorProvider = operatorProvider ?? (() => null);
        _triggers = Channel.CreateUnbounded<RuntimeTriggerMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _cameraFrames = Channel.CreateBounded<CameraFrameArrivalMessage>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _imageSaveJobs = Channel.CreateBounded<InspectionImageSaveJob>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _pumpTask = Task.Run(() => PumpAsync(_cts.Token));
        _imageSaveTask = Task.Run(() => RunImageSaveLoopAsync(_cts.Token));
    }

    public event Action<InspectionRuntimeFrameResult>? FrameProcessed;
    public event Action<InspectionTaskSessionViewModel, string>? TaskFailed;

    public bool IsRunning(InspectionTaskSessionViewModel task) => _tasks.ContainsKey(task.Id);

    public Task StartTaskAsync(InspectionTaskSessionViewModel task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (_tasks.ContainsKey(task.Id))
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            var cameras = new List<RunningCameraContext>();
            try
            {
                foreach (var camera in task.Cameras)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    cameras.Add(StartCamera(camera));
                }

                var context = new RunningTaskContext(task, cameras);
                ValidateTaskConfiguration(context);
                if (task.TriggerMode == InspectionTaskTriggerMode.CameraCallback)
                {
                    ClearCallbackFrames(context);
                }

                context.MarkStarted();
                if (!_tasks.TryAdd(task.Id, context))
                {
                    return;
                }

                WarmupTaskAction(task);
                task.IsRunning = true;
                task.StatusText = "\u8fd0\u884c\u4e2d";
                task.SummaryMessage = task.TriggerMode == InspectionTaskTriggerMode.CameraCallback
                    ? "\u4efb\u52a1\u5df2\u542f\u52a8\uff0c\u7b49\u5f85\u76f8\u673a\u786c\u89e6\u53d1\u3002"
                    : "\u4efb\u52a1\u5df2\u542f\u52a8\uff0c\u7b49\u5f85\u89e6\u53d1\u3002";
            }
            catch
            {
                if (_tasks.TryRemove(task.Id, out var addedContext))
                {
                    ReleaseUnusedCameras(addedContext.Cameras);
                }
                else
                {
                    ReleaseUnusedCameras(cameras);
                }

                task.IsRunning = false;
                task.StatusText = "\u542f\u52a8\u5931\u8d25";
                throw;
            }
        }, cancellationToken);
    }

    public void StopTask(InspectionTaskSessionViewModel task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (!_tasks.TryRemove(task.Id, out var context))
        {
            return;
        }

        ReleaseUnusedCameras(context.Cameras);

        task.IsRunning = false;
        task.StatusText = "\u5df2\u505c\u6b62";
        task.SummaryMessage = "\u4efb\u52a1\u5df2\u505c\u6b62\u3002";
    }

    private void ReleaseUnusedCameras(IEnumerable<RunningCameraContext> cameras)
    {
        foreach (var camera in cameras)
        {
            if (_tasks.Values.Any(item => item.Cameras.Any(candidate => ReferenceEquals(candidate, camera))))
            {
                continue;
            }

            if (_cameras.TryRemove(camera.Camera.Id, out var removed))
            {
                removed.Dispose();
            }
        }
    }

    private void WarmupTaskAction(InspectionTaskSessionViewModel task)
    {
        try
        {
            var action = _actionRegistry.Resolve(task.ActionType);
            if (action is IWarmupInspectionAction warmupAction)
            {
                warmupAction.Warmup(new InspectionRecipeKey(task.ProductModel, task.RecipeTaskId, task.PositionNo));
            }
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Error("inspection-runtime", $"Task '{task.Name}' warmup failed.", ex);
        }
    }

    public bool TryTrigger(InspectionTaskSessionViewModel task, string source = "manual")
    {
        ArgumentNullException.ThrowIfNull(task);
        return TryTrigger(task, source, out _);
    }

    public bool TryTrigger(InspectionTaskSessionViewModel task, string source, out Task<InspectionRuntimeTaskResult> completion)
    {
        ArgumentNullException.ThrowIfNull(task);
        completion = Task.FromResult(InspectionRuntimeTaskResult.NotStarted(task.Id));
        if (!IsRunning(task))
        {
            return false;
        }

        if (task.TriggerMode != InspectionTaskTriggerMode.TriggerCommand)
        {
            return false;
        }

        var trigger = new InspectionTriggerEventArgs
        {
            Source = source,
            StationId = task.StationId,
            TaskId = task.Id,
            ProductModel = task.ProductModel,
            PositionNo = task.PositionNo,
            TriggerId = $"{source}-{DateTimeOffset.Now:yyyyMMddHHmmssfff}",
            TriggerTime = DateTimeOffset.Now
        };
        var tcs = new TaskCompletionSource<InspectionRuntimeTaskResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_triggers.Writer.TryWrite(new RuntimeTriggerMessage(trigger, tcs)))
        {
            tcs.TrySetResult(InspectionRuntimeTaskResult.NotStarted(trigger.TriggerId ?? task.Id));
            return false;
        }

        completion = tcs.Task;
        return true;
    }

    private RunningCameraContext StartCamera(InspectionCameraSessionViewModel camera)
    {
        lock (_cameraStartSync)
        {
            if (_cameras.TryGetValue(camera.Id, out var existing))
            {
                return existing;
            }

            try
            {
                var session = _cameraProviders.Open(camera.Profile.BuildOpenOptions());
                camera.IsRunning = true;
                camera.StatusText = $"\u5df2\u8fde\u63a5: {session.DisplayName} / {session.TriggerMode}";
                var context = new RunningCameraContext(camera, session);
                context.FrameArrived += OnCameraFrameArrived;
                if (_cameras.TryAdd(camera.Id, context))
                {
                    return context;
                }

                context.FrameArrived -= OnCameraFrameArrived;
                context.Dispose();
                return _cameras[camera.Id];
            }
            catch (Exception ex)
            {
                camera.IsRunning = false;
                camera.StatusText = $"\u8fde\u63a5\u5931\u8d25: {ex.Message}";
                throw;
            }
        }
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var triggerTask = _triggers.Reader.WaitToReadAsync(cancellationToken).AsTask();
                var frameTask = _cameraFrames.Reader.WaitToReadAsync(cancellationToken).AsTask();
                var completed = await Task.WhenAny(triggerTask, frameTask).ConfigureAwait(false);
                if (completed == triggerTask && await triggerTask.ConfigureAwait(false))
                {
                    while (_triggers.Reader.TryRead(out var trigger))
                    {
                        await ProcessTriggerAsync(trigger, cancellationToken).ConfigureAwait(false);
                    }

                    continue;
                }

                if (await frameTask.ConfigureAwait(false))
                {
                    while (_cameraFrames.Reader.TryRead(out var frame))
                    {
                        await ProcessCameraFrameAsync(frame, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ProcessTriggerAsync(RuntimeTriggerMessage triggerMessage, CancellationToken cancellationToken)
    {
        var targets = ResolveTargets(triggerMessage.Trigger)
            .Where(target => target.Task.TriggerMode == InspectionTaskTriggerMode.TriggerCommand)
            .ToArray();
        var decisions = new List<InspectionCycleDecision>();
        foreach (var target in targets)
        {
            var result = await ExecuteTaskAsync(target, triggerMessage.Trigger, RuntimeCaptureMode.TriggerCommand, cancellationToken)
                .ConfigureAwait(false);
            decisions.Add(result.Decision);
        }

        triggerMessage.Completion?.TrySetResult(new InspectionRuntimeTaskResult(
            targets.Length > 0,
            AggregateDecision(decisions),
            triggerMessage.Trigger.TriggerId ?? string.Empty,
            triggerMessage.Trigger.TriggerTime));
    }

    private async Task ProcessCameraFrameAsync(CameraFrameArrivalMessage frame, CancellationToken cancellationToken)
    {
        var targets = _tasks.Values
            .Where(task => IsFrameForCallbackTask(task, frame))
            .ToArray();

        foreach (var target in targets)
        {
            await ExecuteCallbackTaskAsync(target, frame, cancellationToken).ConfigureAwait(false);
        }
    }

    private IReadOnlyList<RunningTaskContext> ResolveTargets(InspectionTriggerEventArgs trigger)
    {
        var hasFilter = !string.IsNullOrWhiteSpace(trigger.StationId) ||
                        !string.IsNullOrWhiteSpace(trigger.TaskId);
        var targets = _tasks.Values.Where(task =>
        {
            if (!string.IsNullOrWhiteSpace(trigger.StationId) &&
                !string.Equals(task.Task.StationId, trigger.StationId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(trigger.TaskId) &&
                !string.Equals(task.Task.RecipeTaskId, trigger.TaskId, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(task.Task.Id, trigger.TaskId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }).ToArray();

        return targets.Length > 0 || hasFilter ? targets : System.Linq.Enumerable.ToArray(_tasks.Values);
    }

    private async Task<InspectionRuntimeTaskResult> ExecuteTaskAsync(
        RunningTaskContext context,
        InspectionTriggerEventArgs trigger,
        RuntimeCaptureMode captureMode,
        CancellationToken cancellationToken)
    {
        var captureResult = await CaptureTaskFramesAsync(context, captureMode, cancellationToken).ConfigureAwait(false);
        var decisions = new List<InspectionCycleDecision>(captureResult.Decisions);
        if (cancellationToken.IsCancellationRequested)
        {
            DisposeCapturedFrames(captureResult.Frames);
            return new InspectionRuntimeTaskResult(
                decisions.Count > 0,
                AggregateDecision(decisions),
                trigger.TriggerId ?? string.Empty,
                trigger.TriggerTime);
        }

        var primaryCameraId = ResolvePrimaryCameraId(context);
        var productPresence = EvaluateProductPresence(captureResult.Frames, primaryCameraId);
        if (productPresence.ShouldSkipInspection)
        {
            var skippedTasks = captureResult.Frames
                .Select(frame => Task.Run(() => ExecutePresenceSkippedFrame(frame, context.Task, trigger, productPresence, primaryCameraId), CancellationToken.None))
                .ToArray();
            var skippedDecisions = await Task.WhenAll(skippedTasks).ConfigureAwait(false);
            decisions.AddRange(skippedDecisions);
            return new InspectionRuntimeTaskResult(
                decisions.Count > 0,
                AggregateDecision(decisions),
                trigger.TriggerId ?? string.Empty,
                trigger.TriggerTime);
        }

        var executionTasks = captureResult.Frames
            .Select(frame => Task.Run(
                () => ExecuteCapturedFrame(
                    frame,
                    context.Task,
                    trigger,
                    IsPrimaryCamera(frame.Camera.Camera.Id, primaryCameraId) ? productPresence : ProductPresenceInspectionResult.Disabled,
                    primaryCameraId),
                CancellationToken.None))
            .ToArray();
        var executionDecisions = await Task.WhenAll(executionTasks).ConfigureAwait(false);
        foreach (var decision in executionDecisions)
        {
            if (decision.HasValue)
            {
                decisions.Add(decision.Value);
            }
        }

        return new InspectionRuntimeTaskResult(
            decisions.Count > 0,
            AggregateDecision(decisions),
            trigger.TriggerId ?? string.Empty,
            trigger.TriggerTime);
    }

    private async Task<TaskCaptureResult> CaptureTaskFramesAsync(
        RunningTaskContext context,
        RuntimeCaptureMode captureMode,
        CancellationToken cancellationToken)
    {
        var acquired = new List<RunningCameraContext>(context.Cameras.Count);
        var decisions = new ConcurrentBag<InspectionCycleDecision>();
        try
        {
            foreach (var camera in context.Cameras)
            {
                await camera.CaptureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquired.Add(camera);
            }

            var slots = acquired
                .Select(camera => new CameraCaptureSlot(camera))
                .ToArray();

            if (captureMode == RuntimeCaptureMode.TriggerCommand)
            {
                var triggerTasks = slots
                    .Where(slot => slot.Camera.Session.TriggerMode == CameraTriggerMode.Software)
                    .Select(slot => Task.Run(() => TryTriggerCamera(slot, context.Task, decisions, cancellationToken)))
                    .ToArray();
                await Task.WhenAll(triggerTasks).ConfigureAwait(false);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return new TaskCaptureResult(Array.Empty<CapturedCameraFrame>(), decisions.ToArray());
            }

            var readTasks = slots
                .Where(slot => slot.ShouldRead)
                .Select(slot => Task.Run(() => TryReadCameraFrame(slot, context.Task, decisions, cancellationToken)))
                .ToArray();
            var frames = await Task.WhenAll(readTasks).ConfigureAwait(false);
            return new TaskCaptureResult(frames.Where(frame => frame != null).Select(frame => frame!).ToArray(), decisions.ToArray());
        }
        finally
        {
            foreach (var camera in acquired)
            {
                camera.CaptureLock.Release();
            }
        }
    }

    private void TryTriggerCamera(
        CameraCaptureSlot slot,
        InspectionTaskSessionViewModel task,
        ConcurrentBag<InspectionCycleDecision> decisions,
        CancellationToken cancellationToken)
    {
        try
        {
            slot.Start();
            if (!slot.Camera.Session.TryTrigger(cancellationToken))
            {
                slot.ShouldRead = false;
                slot.Stop();
            }
        }
        catch (Exception ex)
        {
            slot.ShouldRead = false;
            slot.Stop();
            if (!cancellationToken.IsCancellationRequested)
            {
                CameraDiagnostics.Error("inspection-runtime", $"Task '{task.Name}' failed.", ex);
                TaskFailed?.Invoke(task, ex.Message);
                decisions.Add(InspectionCycleDecision.Unknown);
            }
        }
    }

    private CapturedCameraFrame? TryReadCameraFrame(
        CameraCaptureSlot slot,
        InspectionTaskSessionViewModel task,
        ConcurrentBag<InspectionCycleDecision> decisions,
        CancellationToken cancellationToken)
    {
        var image = new Mat();
        try
        {
            slot.StartIfNeeded();
            var captured = slot.Camera.Session.TryRead(image, cancellationToken, out _);
            slot.Stop();
            if (!captured)
            {
                image.Dispose();
                return null;
            }

            return new CapturedCameraFrame(
                slot.Camera,
                image,
                slot.CaptureWatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            slot.Stop();
            image.Dispose();
            if (!cancellationToken.IsCancellationRequested)
            {
                CameraDiagnostics.Error("inspection-runtime", $"Task '{task.Name}' failed.", ex);
                TaskFailed?.Invoke(task, ex.Message);
                decisions.Add(InspectionCycleDecision.Unknown);
            }

            return null;
        }
    }

    private async Task ExecuteCallbackTaskAsync(
        RunningTaskContext context,
        CameraFrameArrivalMessage firstFrame,
        CancellationToken cancellationToken)
    {
        if (!context.CallbackGate.Wait(0))
        {
            return;
        }

        var deferredFrames = new List<CameraFrameArrivalMessage>();
        try
        {
            var triggerTime = firstFrame.Metadata.CaptureUtcMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(firstFrame.Metadata.CaptureUtcMs)
                : DateTimeOffset.Now;
            var trigger = new InspectionTriggerEventArgs
            {
                Source = "camera-callback",
                StationId = context.Task.StationId,
                TaskId = context.Task.RecipeTaskId,
                ProductModel = context.Task.ProductModel,
                PositionNo = context.Task.PositionNo,
                TriggerId = $"camera-callback-{triggerTime:yyyyMMddHHmmssfff}",
                TriggerTime = triggerTime
            };

            if (!await WaitForCallbackFrameSetAsync(context, firstFrame, deferredFrames, cancellationToken).ConfigureAwait(false))
            {
                ClearCallbackFrames(context);
                var message = $"Camera callback frame set timed out for task '{context.Task.Name}'.";
                CameraDiagnostics.Error("inspection-runtime", message);
                TaskFailed?.Invoke(context.Task, message);
                return;
            }

            await ExecuteTaskAsync(context, trigger, RuntimeCaptureMode.ReadExistingFrame, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            context.CallbackGate.Release();
        }

        foreach (var deferredFrame in deferredFrames)
        {
            await ProcessCameraFrameAsync(deferredFrame, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> WaitForCallbackFrameSetAsync(
        RunningTaskContext context,
        CameraFrameArrivalMessage firstFrame,
        List<CameraFrameArrivalMessage> deferredFrames,
        CancellationToken cancellationToken)
    {
        const int timeoutMs = 1500;
        var pending = context.Cameras
            .Where(camera => !ReferenceEquals(camera, firstFrame.Camera))
            .ToDictionary(camera => camera.Camera.Id, StringComparer.OrdinalIgnoreCase);
        if (pending.Count == 0)
        {
            return true;
        }

        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        while (pending.Count > 0 && DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            var remainingMs = Math.Max(1, (int)(deadline - DateTimeOffset.UtcNow).TotalMilliseconds);
            var readTask = _cameraFrames.Reader.ReadAsync(cancellationToken).AsTask();
            if (await Task.WhenAny(readTask, Task.Delay(remainingMs, cancellationToken)).ConfigureAwait(false) != readTask)
            {
                break;
            }

            var frame = await readTask.ConfigureAwait(false);
            if (frame.ArrivedAtUtc < context.StartedAtUtc)
            {
                continue;
            }

            if (!context.Cameras.Any(camera => ReferenceEquals(camera, frame.Camera)))
            {
                deferredFrames.Add(frame);
                continue;
            }

            if (!pending.Remove(frame.Camera.Camera.Id))
            {
                deferredFrames.Add(frame);
            }
        }

        return pending.Count == 0;
    }

    private InspectionCycleDecision? ExecuteCapturedFrame(
        CapturedCameraFrame capturedFrame,
        InspectionTaskSessionViewModel task,
        InspectionTriggerEventArgs trigger,
        ProductPresenceInspectionResult productPresence,
        string primaryCameraId)
    {
        using var image = capturedFrame.Image;
        var totalWatch = Stopwatch.StartNew();
        try
        {
            var productModel = NormalizeRecipePart(trigger.ProductModel, task.ProductModel);
            var positionNo = NormalizeRecipePart(trigger.PositionNo, task.PositionNo);
            var action = _actionRegistry.Resolve(task.ActionType);
            var actionWatch = Stopwatch.StartNew();
            var result = action.Execute(new InspectionRequest
            {
                OriginalImage = image,
                ProductModel = productModel,
                TaskId = task.RecipeTaskId,
                PositionNo = positionNo,
                StationId = task.StationId,
                TaskInstanceId = task.Id,
                CameraId = capturedFrame.Camera.Camera.Id,
                ActionType = task.ActionType,
                TriggerId = trigger.TriggerId,
                TriggerTime = trigger.TriggerTime,
                Operator = _operatorProvider()
            });
            result = AttachProductPresenceResult(
                result,
                productPresence,
                capturedFrame.Camera.Camera.Id,
                primaryCameraId);
            actionWatch.Stop();

            var postprocessWatch = Stopwatch.StartNew();
            var imageSavePlan = QueueImageSave(
                capturedFrame.Camera.Camera.Profile,
                image,
                result.ResolvedRois,
                productModel,
                task.RecipeTaskId,
                positionNo,
                trigger.TriggerTime);
            var frameImage = CreateDisplayImageSource(image, result);
            postprocessWatch.Stop();
            totalWatch.Stop();
            var timing = new InspectionRuntimeTiming(
                capturedFrame.CaptureMs,
                actionWatch.Elapsed.TotalMilliseconds,
                postprocessWatch.Elapsed.TotalMilliseconds,
                capturedFrame.CaptureMs + totalWatch.Elapsed.TotalMilliseconds);
            FrameProcessed?.Invoke(new InspectionRuntimeFrameResult(
                task,
                capturedFrame.Camera.Camera,
                result,
                frameImage,
                image.Width,
                image.Height,
                $"Trigger completed: {trigger.TriggerTime:HH:mm:ss.fff}",
                timing,
                imageSavePlan.ExpectedRoiCount,
                imageSavePlan.ImagePath,
                imageSavePlan.RoiImagePaths));
            return result.Decision;
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Error("inspection-runtime", $"Task '{task.Name}' failed.", ex);
            TaskFailed?.Invoke(task, ex.Message);
            return InspectionCycleDecision.Unknown;
        }
    }

    private ProductPresenceInspectionResult EvaluateProductPresence(
        IReadOnlyList<CapturedCameraFrame> frames,
        string primaryCameraId)
    {
        if (string.IsNullOrWhiteSpace(primaryCameraId))
        {
            return ProductPresenceInspectionResult.Disabled;
        }

        var primaryFrame = frames.FirstOrDefault(frame => IsPrimaryCamera(frame.Camera.Camera.Id, primaryCameraId));
        if (primaryFrame == null)
        {
            return ProductPresenceInspectionResult.Disabled;
        }

        var result = _productPresenceGate.Evaluate(primaryFrame.Image);
        if (!result.Enabled)
        {
            return result;
        }

        return AttachPresenceCameraMetadata(result, primaryCameraId, primaryCameraId);
    }

    private InspectionCycleDecision ExecutePresenceSkippedFrame(
        CapturedCameraFrame capturedFrame,
        InspectionTaskSessionViewModel task,
        InspectionTriggerEventArgs trigger,
        ProductPresenceInspectionResult productPresence,
        string primaryCameraId)
    {
        using var image = capturedFrame.Image;
        var totalWatch = Stopwatch.StartNew();
        try
        {
            var productModel = NormalizeRecipePart(trigger.ProductModel, task.ProductModel);
            var positionNo = NormalizeRecipePart(trigger.PositionNo, task.PositionNo);
            var cameraId = capturedFrame.Camera.Camera.Id;
            var cameraPresence = AttachPresenceCameraMetadata(
                productPresence.ForSkippedCamera(cameraId, primaryCameraId),
                cameraId,
                primaryCameraId);
            var result = CreateProductPresenceSkippedResult(
                task,
                trigger,
                productModel,
                positionNo,
                cameraId,
                cameraPresence,
                _operatorProvider());

            var postprocessWatch = Stopwatch.StartNew();
            var imageSavePlan = QueueImageSave(
                capturedFrame.Camera.Camera.Profile,
                image,
                result.ResolvedRois,
                productModel,
                task.RecipeTaskId,
                positionNo,
                trigger.TriggerTime);
            var frameImage = CreateDisplayImageSource(image, result);
            postprocessWatch.Stop();
            totalWatch.Stop();

            var timing = new InspectionRuntimeTiming(
                capturedFrame.CaptureMs,
                0,
                postprocessWatch.Elapsed.TotalMilliseconds,
                capturedFrame.CaptureMs + totalWatch.Elapsed.TotalMilliseconds);
            FrameProcessed?.Invoke(new InspectionRuntimeFrameResult(
                task,
                capturedFrame.Camera.Camera,
                result,
                frameImage,
                image.Width,
                image.Height,
                $"Trigger completed: {trigger.TriggerTime:HH:mm:ss.fff}",
                timing,
                imageSavePlan.ExpectedRoiCount,
                imageSavePlan.ImagePath,
                imageSavePlan.RoiImagePaths));
            return result.Decision;
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Error("inspection-runtime", $"Task '{task.Name}' product presence skip failed.", ex);
            TaskFailed?.Invoke(task, ex.Message);
            return InspectionCycleDecision.Unknown;
        }
    }

    private static InspectionCycleResult AttachProductPresenceResult(
        InspectionCycleResult result,
        ProductPresenceInspectionResult productPresence,
        string cameraId,
        string primaryCameraId)
    {
        if (!productPresence.Enabled)
        {
            return result;
        }

        var attached = AttachPresenceCameraMetadata(productPresence, cameraId, primaryCameraId);
        var metadata = MergeMetadata(result.Metadata, attached.Metadata);
        var summary = string.IsNullOrWhiteSpace(result.SummaryMessage)
            ? attached.SummaryText
            : $"{attached.SummaryText}{Environment.NewLine}{result.SummaryMessage}";
        return result with
        {
            SummaryMessage = summary,
            Metadata = metadata
        };
    }

    private static ProductPresenceInspectionResult AttachPresenceCameraMetadata(
        ProductPresenceInspectionResult productPresence,
        string cameraId,
        string primaryCameraId)
    {
        if (!productPresence.Enabled)
        {
            return productPresence;
        }

        var metadata = new Dictionary<string, string>(productPresence.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["presence.cameraId"] = cameraId,
            ["presence.primaryCameraId"] = primaryCameraId,
            ["presence.skipped"] = productPresence.ShouldSkipInspection ? "true" : "false"
        };
        return productPresence with { Metadata = metadata };
    }

    private static IReadOnlyDictionary<string, string> MergeMetadata(
        IReadOnlyDictionary<string, string> existing,
        IReadOnlyDictionary<string, string> additions)
    {
        var metadata = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
        foreach (var item in additions)
        {
            metadata[item.Key] = item.Value;
        }

        return metadata;
    }

    private static InspectionCycleResult CreateProductPresenceSkippedResult(
        InspectionTaskSessionViewModel task,
        InspectionTriggerEventArgs trigger,
        string productModel,
        string positionNo,
        string cameraId,
        ProductPresenceInspectionResult productPresence,
        InspectionOperatorSnapshot? operatorSnapshot)
    {
        return new InspectionCycleResult
        {
            RecipeKey = new InspectionRecipeKey(productModel, task.RecipeTaskId, positionNo),
            StationId = task.StationId,
            TaskInstanceId = task.Id,
            CameraId = cameraId,
            ActionType = task.ActionType,
            TriggerId = trigger.TriggerId,
            TriggerTime = trigger.TriggerTime,
            Operator = operatorSnapshot,
            Decision = InspectionCycleDecision.Ng,
            SummaryMessage = productPresence.SummaryText,
            Calibration = CalibrationContext.Empty,
            ResolvedModels = string.IsNullOrWhiteSpace(productPresence.ModelId)
                ? Array.Empty<InspectionModelReference>()
                : new[]
                {
                    new InspectionModelReference
                    {
                        ModelId = productPresence.ModelId,
                        Alias = "product-presence",
                        Sequence = -1
                    }
                },
            ResolvedRois = Array.Empty<RoiDefinition>(),
            RoiResults = Array.Empty<InspectionRoiResult>(),
            Metadata = productPresence.Metadata
        };
    }

    private static string ResolvePrimaryCameraId(RunningTaskContext context)
    {
        return context.Cameras.FirstOrDefault()?.Camera.Id ?? string.Empty;
    }

    private static bool IsPrimaryCamera(string cameraId, string primaryCameraId)
    {
        return !string.IsNullOrWhiteSpace(primaryCameraId) &&
               string.Equals(cameraId, primaryCameraId, StringComparison.OrdinalIgnoreCase);
    }

    private static void DisposeCapturedFrames(IEnumerable<CapturedCameraFrame> frames)
    {
        foreach (var frame in frames)
        {
            frame.Image.Dispose();
        }
    }

    private static void ClearCallbackFrames(RunningTaskContext context)
    {
        foreach (var camera in context.Cameras)
        {
            camera.ClearPendingFrames();
        }
    }

    private static bool IsFrameForCallbackTask(RunningTaskContext context, CameraFrameArrivalMessage frame)
    {
        return context.Task.TriggerMode == InspectionTaskTriggerMode.CameraCallback &&
               frame.ArrivedAtUtc >= context.StartedAtUtc &&
               context.Cameras.Any(camera => ReferenceEquals(camera, frame.Camera));
    }


    public static ImageSource CreateDisplayImageSource(Mat source)
    {
        Mat? converted = null;
        Mat? continuous = null;
        var image = source;
        var format = PixelFormats.Bgr24;

        if (source.Type() == MatType.CV_8UC1)
        {
            format = PixelFormats.Gray8;
        }
        else if (source.Type() == MatType.CV_8UC4)
        {
            format = PixelFormats.Bgra32;
        }
        else if (source.Type() != MatType.CV_8UC3)
        {
            converted = new Mat();
            Cv2.CvtColor(source, converted, ColorConversionCodes.BGRA2BGR);
            image = converted;
        }

        if (!image.IsContinuous())
        {
            continuous = image.Clone();
            image = continuous;
        }

        try
        {
            var stride = checked((int)image.Step());
            var pixels = new byte[checked(stride * image.Height)];
            Marshal.Copy(image.Data, pixels, 0, pixels.Length);
            var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, format, null, pixels, stride);
            bitmap.Freeze();
            return bitmap;
        }
        finally
        {
            converted?.Dispose();
            continuous?.Dispose();
        }
    }

    public static ImageSource CreateDisplayImageSource(Mat source, InspectionCycleResult? result)
    {
        if (!InspectionResultDisplayRenderer.HasSegmentationOverlays(result))
        {
            return CreateDisplayImageSource(source);
        }

        using var rendered = source.Clone();
        InspectionResultDisplayRenderer.DrawSegmentationOverlays(rendered, result);
        return CreateDisplayImageSource(rendered);
    }

    private InspectionImageSavePlan QueueImageSave(
        InspectionCameraProfile profile,
        Mat image,
        IReadOnlyList<RoiDefinition> rois,
        string productModel,
        string taskId,
        string positionNo,
        DateTimeOffset timestamp)
    {
        if (!profile.SaveImages)
        {
            return InspectionImageSavePlan.Empty;
        }

        var plan = BuildImageSavePlan(profile, rois, productModel, taskId, positionNo, timestamp);
        var imageClone = image.Clone();
        var job = new InspectionImageSaveJob(
            profile,
            imageClone,
            rois.ToArray(),
            productModel,
            taskId,
            positionNo,
            timestamp,
            plan.ImagePath ?? string.Empty,
            plan.RoiImagePaths);

        if (_imageSaveJobs.Writer.TryWrite(job))
        {
            return plan;
        }

        imageClone.Dispose();
        CameraDiagnostics.Error("inspection-runtime", "Image save queue is full; dropped inspection image save job.");
        return InspectionImageSavePlan.Empty;
    }

    private async Task RunImageSaveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var job in _imageSaveJobs.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                using (job.Image)
                {
                    TrySaveImages(job);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static int TrySaveImages(InspectionImageSaveJob job)
    {
        if (!job.Profile.SaveImages || string.IsNullOrWhiteSpace(job.ImagePath))
        {
            return 0;
        }

        try
        {
            var directory = Path.GetDirectoryName(job.ImagePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return 0;
            }

            Directory.CreateDirectory(directory);
            SaveJpegImage(job.ImagePath, job.Image);
            return job.Profile.SaveRoiImages
                ? TrySaveRoiImages(job.Image, job.Rois, job.RoiImagePaths)
                : 0;
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Error("inspection-runtime", "Failed to save inspection image.", ex);
            return 0;
        }
    }

    private static int TrySaveRoiImages(
        Mat image,
        IReadOnlyList<RoiDefinition> rois,
        IReadOnlyDictionary<string, string> roiImagePaths)
    {
        if (rois.Count == 0)
        {
            return 0;
        }

        var savedCount = 0;
        foreach (var roi in rois.Where(roi => roi.Enabled).OrderBy(roi => roi.SortOrder))
        {
            try
            {
                using var roiImage = ExtractHorizontalRoi(image, roi);
                if (roiImage.Empty())
                {
                    continue;
                }

                if (!roiImagePaths.TryGetValue(roi.Id, out var roiPath) ||
                    string.IsNullOrWhiteSpace(roiPath))
                {
                    continue;
                }

                var roiDirectory = Path.GetDirectoryName(roiPath);
                if (string.IsNullOrWhiteSpace(roiDirectory))
                {
                    continue;
                }

                Directory.CreateDirectory(roiDirectory);
                SaveJpegImage(roiPath, roiImage);
                savedCount++;
            }
            catch (Exception ex)
            {
                CameraDiagnostics.Error("inspection-runtime", $"Failed to save ROI image '{roi.Name}'.", ex);
            }
        }

        return savedCount;
    }

    private static InspectionImageSavePlan BuildImageSavePlan(
        InspectionCameraProfile profile,
        IReadOnlyList<RoiDefinition> rois,
        string productModel,
        string taskId,
        string positionNo,
        DateTimeOffset timestamp)
    {
        if (!profile.SaveImages)
        {
            return InspectionImageSavePlan.Empty;
        }

        var directory = BuildRecipeImageDirectory(profile, productModel, taskId, positionNo, timestamp);
        var fileName = BuildImageFileName(profile.ImageFileNamePattern, productModel, taskId, positionNo, timestamp);
        var imagePath = Path.Combine(directory, fileName);
        if (!profile.SaveRoiImages)
        {
            return new InspectionImageSavePlan(0, imagePath, new Dictionary<string, string>());
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var roiPaths = rois
            .Where(roi => roi.Enabled)
            .OrderBy(roi => roi.SortOrder)
            .ToDictionary(
                roi => roi.Id,
                roi =>
                {
                    var roiName = SanitizePathPart(string.IsNullOrWhiteSpace(roi.Name) ? roi.Id : roi.Name);
                    var roiDirectory = Path.Combine(directory, "ROI", roiName);
                    return Path.Combine(roiDirectory, $"{baseName}_{roi.SortOrder:000}.jpg");
                },
                StringComparer.OrdinalIgnoreCase);
        return new InspectionImageSavePlan(roiPaths.Count, imagePath, roiPaths);
    }

    private static Mat ExtractHorizontalRoi(Mat image, RoiDefinition roi)
    {
        var centerX = roi.CenterX * image.Width;
        var centerY = roi.CenterY * image.Height;
        var width = Math.Max(1, (int)Math.Round(roi.Width * image.Width));
        var height = Math.Max(1, (int)Math.Round(roi.Height * image.Height));
        using var rotation = Cv2.GetRotationMatrix2D(new Point2f((float)centerX, (float)centerY), roi.AngleDeg, 1.0);
        using var rotated = new Mat();
        Cv2.WarpAffine(
            image,
            rotated,
            rotation,
            new OpenCvSharp.Size(image.Width, image.Height),
            InterpolationFlags.Linear,
            BorderTypes.Replicate);

        var patch = new Mat();
        Cv2.GetRectSubPix(rotated, new OpenCvSharp.Size(width, height), new Point2f((float)centerX, (float)centerY), patch);
        return patch;
    }

    private static void SaveJpegImage(string path, Mat image)
    {
        Cv2.ImWrite(path, image, JpegEncodingParams);
    }

    private static string BuildRecipeImageDirectory(
        InspectionCameraProfile profile,
        string productModel,
        string taskId,
        string positionNo,
        DateTimeOffset timestamp)
    {
        return Path.Combine(
            Path.GetFullPath(profile.ImageSaveDirectory),
            timestamp.LocalDateTime.ToString("yyyy-MM-dd"),
            SanitizePathPart(productModel),
            SanitizePathPart(taskId),
            SanitizePathPart(profile.Name),
            SanitizePathPart(positionNo));
    }

    private static string BuildImageFileName(
        string pattern,
        string productModel,
        string taskId,
        string positionNo,
        DateTimeOffset timestamp)
    {
        var value = string.IsNullOrWhiteSpace(pattern)
            ? "{ProductModel}_{TaskId}_{PositionNo}_{Timestamp:yyyyMMdd_HHmmssfff}.jpg"
            : pattern.Trim();
        value = Regex.Replace(value, "\\{Timestamp(?::([^}]+))?\\}", match =>
        {
            var format = match.Groups[1].Success ? match.Groups[1].Value : "yyyyMMdd_HHmmssfff";
            return timestamp.ToString(format);
        });
        value = value
            .Replace("{ProductModel}", productModel)
            .Replace("{TaskId}", taskId)
            .Replace("{PositionNo}", positionNo);

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return Path.ChangeExtension(value, ".jpg");
    }

    private static string SanitizePathPart(string value)
    {
        var safe = string.IsNullOrWhiteSpace(value) ? "_" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(invalid, '_');
        }

        return safe;
    }

    private static string NormalizeRecipePart(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private void OnCameraFrameArrived(object? sender, CameraFrameArrivalMessage message)
    {
        _cameraFrames.Writer.TryWrite(message);
    }

    private static InspectionCycleDecision AggregateDecision(IEnumerable<InspectionCycleDecision> decisions)
    {
        var values = decisions.ToArray();
        if (values.Any(decision => decision == InspectionCycleDecision.Ng))
        {
            return InspectionCycleDecision.Ng;
        }

        if (values.Any(decision => decision == InspectionCycleDecision.Warning))
        {
            return InspectionCycleDecision.Warning;
        }

        if (values.Any(decision => decision == InspectionCycleDecision.Ok))
        {
            return InspectionCycleDecision.Ok;
        }

        return InspectionCycleDecision.Unknown;
    }

    private void ValidateTaskConfiguration(RunningTaskContext context)
    {
        if (context.Cameras.Count == 0)
        {
            throw new InvalidOperationException($"Task '{context.Task.Name}' has no cameras.");
        }

        if (context.Task.TriggerMode == InspectionTaskTriggerMode.CameraCallback)
        {
            foreach (var camera in context.Cameras)
            {
                if (camera.Camera.ProviderId != CameraProviderIds.HikRobot ||
                    camera.Camera.TriggerMode != CameraTriggerMode.HardwareLine0 ||
                    camera.Session is not ICameraFrameCallbackSource)
                {
                    throw new InvalidOperationException(
                        $"Task '{context.Task.Name}' camera trigger modes are inconsistent; camera callback tasks require every camera to be HikRobot hardware trigger (auto input line).");
                }
            }

            var shared = context.Cameras.Any(camera => _tasks.Values.Any(task =>
                task.Task.TriggerMode == InspectionTaskTriggerMode.CameraCallback &&
                task.Cameras.Any(existing => ReferenceEquals(existing, camera))));
            if (shared)
            {
                throw new InvalidOperationException(
                    $"Task '{context.Task.Name}' uses camera callback trigger; callback cameras cannot be shared by another running callback task.");
            }

            return;
        }

        if (context.Cameras.Any(camera => camera.Camera.TriggerMode == CameraTriggerMode.HardwareLine0))
        {
            throw new InvalidOperationException(
                $"Task '{context.Task.Name}' camera trigger modes are inconsistent; hardware trigger cameras require camera callback task mode, and cannot be mixed with command capture cameras.");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _triggers.Writer.TryComplete();
        _cameraFrames.Writer.TryComplete();
        _imageSaveJobs.Writer.TryComplete();
        try
        {
            _pumpTask.Wait(TimeSpan.FromSeconds(2));
            _imageSaveTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        foreach (var task in _tasks.Values)
        {
            task.Task.IsRunning = false;
        }

        foreach (var camera in _cameras.Values)
        {
            camera.Dispose();
        }

        if (_productPresenceGate is IDisposable productPresenceGate)
        {
            productPresenceGate.Dispose();
        }

        _cts.Dispose();
    }

    private sealed class RunningTaskContext
    {
        public RunningTaskContext(InspectionTaskSessionViewModel task, IReadOnlyList<RunningCameraContext> cameras)
        {
            Task = task;
            Cameras = cameras;
        }

        public InspectionTaskSessionViewModel Task { get; }

        public IReadOnlyList<RunningCameraContext> Cameras { get; }

        public DateTimeOffset StartedAtUtc { get; private set; } = DateTimeOffset.MaxValue;

        public SemaphoreSlim CallbackGate { get; } = new(1, 1);

        public void MarkStarted()
        {
            StartedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    private sealed class RunningCameraContext : IDisposable
    {
        private readonly ICameraFrameCallbackSource? _callbackSource;

        public RunningCameraContext(InspectionCameraSessionViewModel camera, ICameraSession session)
        {
            Camera = camera;
            Session = session;
            _callbackSource = session as ICameraFrameCallbackSource;
            if (_callbackSource != null)
            {
                _callbackSource.FrameArrived += OnSessionFrameArrived;
            }
        }

        public InspectionCameraSessionViewModel Camera { get; }

        public ICameraSession Session { get; }

        public SemaphoreSlim CaptureLock { get; } = new(1, 1);

        public event EventHandler<CameraFrameArrivalMessage>? FrameArrived;

        public void ClearPendingFrames()
        {
            _callbackSource?.ClearPendingFrames();
        }

        public void Dispose()
        {
            Camera.MarkIdle();
            if (_callbackSource != null)
            {
                _callbackSource.FrameArrived -= OnSessionFrameArrived;
            }

            CaptureLock.Dispose();
            Session.Dispose();
        }

        private void OnSessionFrameArrived(object? sender, CameraFrameArrivedEventArgs e)
        {
            FrameArrived?.Invoke(this, new CameraFrameArrivalMessage(this, e.Metadata, DateTimeOffset.UtcNow));
        }
    }

    private sealed record RuntimeTriggerMessage(
        InspectionTriggerEventArgs Trigger,
        TaskCompletionSource<InspectionRuntimeTaskResult>? Completion);

    private sealed record CameraFrameArrivalMessage(
        RunningCameraContext Camera,
        CameraFrameMetadata Metadata,
        DateTimeOffset ArrivedAtUtc);

    private sealed record TaskCaptureResult(
        IReadOnlyList<CapturedCameraFrame> Frames,
        IReadOnlyList<InspectionCycleDecision> Decisions);

    private sealed record CapturedCameraFrame(
        RunningCameraContext Camera,
        Mat Image,
        double CaptureMs);

    private sealed class CameraCaptureSlot
    {
        private readonly Stopwatch _captureWatch = new();

        public CameraCaptureSlot(RunningCameraContext camera)
        {
            Camera = camera;
        }

        public RunningCameraContext Camera { get; }

        public bool ShouldRead { get; set; } = true;

        public Stopwatch CaptureWatch => _captureWatch;

        public void Start()
        {
            _captureWatch.Restart();
        }

        public void StartIfNeeded()
        {
            if (!_captureWatch.IsRunning && _captureWatch.ElapsedTicks == 0)
            {
                _captureWatch.Start();
            }
        }

        public void Stop()
        {
            _captureWatch.Stop();
        }
    }

    private enum RuntimeCaptureMode
    {
        TriggerCommand,
        ReadExistingFrame
    }
}
