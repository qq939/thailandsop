using System.Diagnostics;
using System.Globalization;
using System.IO;
using OpenCvSharp;
using RapidOcrNet;
using SkiaSharp;

namespace VideoInferenceDemo.ImageInspection.Runtime;

public sealed class RapidOcrPipelineTask : IVisionTask
{
    private readonly RapidOcr _ocr;
    private readonly RapidOcrOptions _options;

    public RapidOcrPipelineTask(VisionTaskDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        TaskId = definition.Id;
        var detPath = GetRequiredFile(definition, "ocr.detPath");
        var clsPath = GetRequiredFile(definition, "ocr.clsPath");
        var recPath = GetRequiredFile(definition, "ocr.recPath");
        var dictPath = GetRequiredFile(definition, "ocr.dictPath");
        var doAngle = ReadBool(definition, "ocr.doAngle");
        var returnWordBox = ReadBool(definition, "ocr.returnWordBox");

        _options = RapidOcrOptions.Default with
        {
            DoAngle = doAngle,
            ReturnWordBox = returnWordBox
        };

        _ocr = new RapidOcr();
        using var sessionOptions = RapidOcr.GetDefaultSessionOptions();
        _ocr.InitModels(detPath, clsPath, recPath, dictPath, sessionOptions);
    }

    public string TaskId { get; }

    public VisionTaskKind TaskKind => VisionTaskKind.OcrPipeline;

    public VisionRuntimeKind RuntimeKind => VisionRuntimeKind.OcrRuntime;

    public string? ActiveDeviceLabel => "RapidOcrNet / CPU";

    public VisionTaskExecutionResult Execute(Mat image, VisionTaskExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Empty())
        {
            return CreateResult(string.Empty, Array.Empty<OcrTextBlock>(), new Dictionary<string, string>
            {
                ["ocr.blockCount"] = "0",
                ["ocr.avgScore"] = "0",
                ["ocr.pipelineMs"] = "0",
                ["ocr.doAngle"] = _options.DoAngle ? "true" : "false",
                ["ocr.returnWordBox"] = _options.ReturnWordBox ? "true" : "false"
            });
        }

        var watch = Stopwatch.StartNew();
        using var bitmap = DecodeMat(image);
        var result = _ocr.Detect(bitmap, _options);
        watch.Stop();

        var blocks = result.TextBlocks?
            .Select(ToPayloadBlock)
            .ToArray() ?? Array.Empty<OcrTextBlock>();
        var avgScore = blocks.Length == 0 ? 0f : blocks.Average(block => block.Score);
        var text = NormalizeText(result.StrRes);
        var metrics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ocr.blockCount"] = blocks.Length.ToString(CultureInfo.InvariantCulture),
            ["ocr.avgScore"] = avgScore.ToString("0.###", CultureInfo.InvariantCulture),
            ["ocr.pipelineMs"] = watch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture),
            ["ocr.dbNetTime"] = result.DbNetTime.ToString("0.0", CultureInfo.InvariantCulture),
            ["ocr.detectTime"] = result.DetectTime.ToString("0.0", CultureInfo.InvariantCulture),
            ["ocr.doAngle"] = _options.DoAngle ? "true" : "false",
            ["ocr.returnWordBox"] = _options.ReturnWordBox ? "true" : "false"
        };

        return CreateResult(text, blocks, metrics);
    }

    public void Warmup(int width, int height)
    {
        using var image = new Mat(Math.Max(32, height), Math.Max(32, width), MatType.CV_8UC3, Scalar.All(255));
        _ = Execute(
            image,
            new VisionTaskExecutionContext(
                new SessionFrameContext(0, 0, 0, 0, image.Width, image.Height),
                new VisionTaskRenderStyle(null, null, 2, 0.8)));
    }

    public void UpdateClassNames(string[]? classNames)
    {
    }

    public bool TryHandleFailure(Exception ex, out string message)
    {
        message = $"OCR pipeline task '{TaskId}' failed: {ex.Message}";
        return false;
    }

    public void Dispose()
    {
        _ocr.Dispose();
    }

    private static VisionTaskExecutionResult CreateResult(
        string text,
        IReadOnlyList<OcrTextBlock> blocks,
        IReadOnlyDictionary<string, string> metrics)
    {
        return new VisionTaskExecutionResult
        {
            Payload = new OcrPayload(text, blocks),
            Annotate = _ => { },
            DeviceLabel = "RapidOcrNet / CPU",
            Metrics = metrics
        };
    }

    private static OcrTextBlock ToPayloadBlock(TextBlock block)
    {
        var score = block.CharScores is { Length: > 0 }
            ? block.CharScores.Average()
            : block.BoxScore;
        var points = block.BoxPoints?
            .Select(point => new OcrPoint(point.X, point.Y))
            .ToArray() ?? Array.Empty<OcrPoint>();

        return new OcrTextBlock(block.Text ?? string.Empty, score, points);
    }

    private static SKBitmap DecodeMat(Mat image)
    {
        if (!Cv2.ImEncode(".png", image, out var bytes) || bytes.Length == 0)
        {
            throw new InvalidOperationException("Failed to encode ROI image for OCR pipeline.");
        }

        return SKBitmap.Decode(bytes)
               ?? throw new InvalidOperationException("Failed to decode ROI image for OCR pipeline.");
    }

    private static string NormalizeText(string? text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }

    private static bool ReadBool(VisionTaskDefinition definition, string key)
    {
        return definition.Metadata.TryGetValue(key, out var raw) &&
               bool.TryParse(raw, out var value) &&
               value;
    }

    private static string GetRequiredFile(VisionTaskDefinition definition, string key)
    {
        var path = definition.Metadata.TryGetValue(key, out var value) ? value?.Trim() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"OCR pipeline model '{definition.Id}' is missing required metadata '{key}'.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"OCR pipeline model '{definition.Id}' file does not exist: {path}", path);
        }

        return path;
    }
}
