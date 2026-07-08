using OpenCvSharp;

namespace VideoInferenceDemo;

public sealed class MediaPipeHandLandmarkTask : IVisionTask, IWorkerStatusProvider
{
    private readonly MediaPipeHandTaskMetadata _metadata;
    private readonly IVisionWorkerHost _workerHost;
    private readonly IVisionWorkerClient _workerClient;

    public MediaPipeHandLandmarkTask(
        VisionTaskDefinition definition,
        MediaPipeHandTaskMetadata metadata,
        IVisionWorkerHost workerHost,
        IVisionWorkerClient workerClient)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _workerHost = workerHost ?? throw new ArgumentNullException(nameof(workerHost));
        _workerClient = workerClient ?? throw new ArgumentNullException(nameof(workerClient));
    }

    public VisionTaskDefinition Definition { get; }
    public string TaskId => Definition.Id;
    public VisionTaskKind TaskKind => VisionTaskKind.HandLandmarks;
    public VisionRuntimeKind RuntimeKind => VisionRuntimeKind.MediaPipe;
    public string? ActiveDeviceLabel => _workerClient.ActiveRuntimeLabel ?? _workerHost.ActiveRuntimeLabel ?? "MediaPipe / Worker";

    public VisionTaskExecutionResult Execute(Mat image, VisionTaskExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(context);

        _workerHost.EnsureStarted();
        var request = BuildRequest(image, context);
        var response = _workerClient.Execute(request, TimeSpan.FromMilliseconds(500));
        if (!response.IsSuccess)
        {
            var status = _workerClient.Status;
            throw new InvalidOperationException(
                $"MediaPipe hand worker returned an error for task '{Definition.Id}': {response.ErrorCode ?? "runtime_error"} / {response.ErrorMessage ?? "Unknown error"} / State={status.State} / Endpoint={status.EndpointName} / LastError={status.LastError ?? "-"}");
        }

        var payload = MediaPipeHandPayloadParser.ParseOrEmpty(response.PayloadJson);
        return new VisionTaskExecutionResult
        {
            Payload = payload,
            Annotate = target => HandLandmarkRenderer.Draw(target, payload, context.RenderStyle),
            DeviceLabel = response.RuntimeLabel ?? ActiveDeviceLabel
        };
    }

    public void Warmup(int width, int height)
    {
        _workerHost.EnsureStarted();
        _workerClient.TryPing(TimeSpan.FromMilliseconds(250));
    }

    public void UpdateClassNames(string[]? classNames)
    {
    }

    public bool TryHandleFailure(Exception ex, out string message)
    {
        var status = _workerClient.Status;
        message = $"MediaPipe hand task failed: {ex.Message} | WorkerState={status.State} | Endpoint={status.EndpointName} | LastError={status.LastError ?? "-"}";
        return false;
    }

    public void Dispose()
    {
        _workerClient.Dispose();
        _workerHost.Dispose();
    }

    public VisionWorkerStatusSnapshot? TryGetWorkerStatus()
    {
        return _workerClient.Status;
    }

    private VisionWorkerRequest BuildRequest(Mat image, VisionTaskExecutionContext context)
    {
        using var bgrFrame = EnsureBgrFrame(image);
        using var resizedFrame = ResizeForWorker(bgrFrame, _metadata.PreferredInputSize);
        var byteLength = checked((int)(resizedFrame.Total() * resizedFrame.ElemSize()));
        var imageBytes = new byte[byteLength];
        System.Runtime.InteropServices.Marshal.Copy(resizedFrame.Data, imageBytes, 0, byteLength);

        return new VisionWorkerRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            FrameId = context.Frame.Sequence,
            TimestampUtc = DateTimeOffset.FromUnixTimeMilliseconds(context.Frame.CaptureUtcMs),
            TaskKind = TaskKind,
            Frame = new VisionWorkerImageFrame
            {
                Width = resizedFrame.Width,
                Height = resizedFrame.Height,
                Stride = (int)resizedFrame.Step(),
                PixelFormat = "BGR24",
                ImageBytes = imageBytes
            },
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["timestampMs"] = ResolveTimestampMs(context).ToString(),
                ["taskFilePath"] = _metadata.TaskFilePath,
                ["maxHands"] = _metadata.MaxHands.ToString(),
                ["preferredInputSize"] = _metadata.PreferredInputSize.ToString()
            }
        };
    }

    private static Mat EnsureBgrFrame(Mat image)
    {
        if (image.Type() == MatType.CV_8UC3 && image.IsContinuous())
        {
            return image.Clone();
        }

        if (image.Channels() == 3)
        {
            return image.Clone();
        }

        var converted = new Mat();
        if (image.Channels() == 4)
        {
            Cv2.CvtColor(image, converted, ColorConversionCodes.BGRA2BGR);
            return converted;
        }

        if (image.Channels() == 1)
        {
            Cv2.CvtColor(image, converted, ColorConversionCodes.GRAY2BGR);
            return converted;
        }

        throw new NotSupportedException(
            $"Unsupported image format for MediaPipe worker. Channels={image.Channels()}, Type={image.Type()}");
    }

    private static Mat ResizeForWorker(Mat image, int preferredInputSize)
    {
        if (preferredInputSize <= 0)
        {
            return image.Clone();
        }

        var maxSide = Math.Max(image.Width, image.Height);
        if (maxSide <= preferredInputSize)
        {
            return image.Clone();
        }

        var scale = preferredInputSize / (double)maxSide;
        var targetWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
        var resized = new Mat();
        Cv2.Resize(image, resized, new Size(targetWidth, targetHeight), 0, 0, InterpolationFlags.Area);
        return resized;
    }

    private static long ResolveTimestampMs(VisionTaskExecutionContext context)
    {
        if (context.Frame.TimelineMs >= 0)
        {
            return context.Frame.TimelineMs;
        }

        if (context.Frame.PtsMs >= 0)
        {
            return context.Frame.PtsMs;
        }

        return Math.Max(0, context.Frame.Sequence);
    }
}
