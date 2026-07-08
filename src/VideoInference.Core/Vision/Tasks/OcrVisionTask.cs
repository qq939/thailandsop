using System.Diagnostics;
using NewLife.Log;
using OpenCvSharp;

namespace VideoInferenceDemo;

public sealed class OcrVisionTask : IVisionTask
{
    private readonly OcrTextRecognizer _recognizer;
    private int _roiX;
    private int _roiY;
    private int _roiWidth;
    private int _roiHeight;

    public OcrVisionTask(
        string taskId,
        string modelPath,
        string dictPath,
        InferenceDeviceKind deviceKind,
        int roiX,
        int roiY,
        int roiWidth,
        int roiHeight,
        int fixedWidth,
        int fixedHeight)
    {
        TaskId = taskId;
        _roiX = roiX;
        _roiY = roiY;
        _roiWidth = roiWidth;
        _roiHeight = roiHeight;
        _recognizer = new OcrTextRecognizer(modelPath, dictPath, deviceKind, fixedWidth, fixedHeight);
    }

    public string TaskId { get; }
    public VisionTaskKind TaskKind => VisionTaskKind.OcrText;
    public VisionRuntimeKind RuntimeKind => VisionRuntimeKind.OcrRuntime;
    public string? ActiveDeviceLabel => _recognizer.ActiveDeviceLabel;

    public VisionTaskExecutionResult Execute(Mat image, VisionTaskExecutionContext context)
    {
        var text = _recognizer.Recognize(image, _roiX, _roiY, _roiWidth, _roiHeight);
        XTrace.WriteLine("[OCR] {0} ROI({1},{2},{3},{4}) → \"{5}\" len={6}", TaskId, _roiX, _roiY, _roiWidth, _roiHeight, text, text.Length);
        var metrics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return new VisionTaskExecutionResult
        {
            Payload = new OcrPayload(text),
            Annotate = frame =>
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    PipelineFrameAnnotator.DrawOcrText(frame, text, _roiX, _roiY, _roiWidth, _roiHeight);
                }
            },
            DeviceLabel = ActiveDeviceLabel,
            Metrics = metrics
        };
    }

    public void Warmup(int width, int height)
    {
        _recognizer.Recognize(new Mat(height, width, MatType.CV_8UC3, Scalar.All(128)),
            _roiX, _roiY, _roiWidth, _roiHeight);
    }

    public void UpdateClassNames(string[]? classNames)
    {
        // OCR task does not use class names
    }

    public bool TryHandleFailure(Exception ex, out string message)
    {
        message = $"OCR task '{TaskId}' failed: {ex.Message}";
        return false;
    }

    public void UpdateRoi(int x, int y, int width, int height)
    {
        Interlocked.Exchange(ref _roiX, x);
        Interlocked.Exchange(ref _roiY, y);
        Interlocked.Exchange(ref _roiWidth, width);
        Interlocked.Exchange(ref _roiHeight, height);
    }

    public void Dispose()
    {
        _recognizer.Dispose();
    }
}
