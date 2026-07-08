using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCvSharp;

namespace VideoInferenceDemo;

public sealed class SequenceBandPrediction
{
    public SequenceBandPrediction(
        int classId,
        string className,
        float confidence,
        int seqStart,
        int seqEnd,
        int x0,
        int y0,
        int x1,
        int y1)
    {
        ClassId = classId;
        ClassName = className;
        Confidence = confidence;
        SeqStart = seqStart;
        SeqEnd = seqEnd;
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
    }

    public int ClassId { get; }
    public string ClassName { get; }
    public float Confidence { get; }
    public int SeqStart { get; }
    public int SeqEnd { get; }
    public int X0 { get; }
    public int Y0 { get; }
    public int X1 { get; }
    public int Y1 { get; }
}

public sealed class SequenceModelMetadata
{
    [JsonPropertyName("checkpoint")]
    public string? Checkpoint { get; init; }

    [JsonPropertyName("onnx_file")]
    public string? OnnxFile { get; init; }

    [JsonPropertyName("input_name")]
    public string InputName { get; init; } = "input";

    [JsonPropertyName("output_name")]
    public string OutputName { get; init; } = "logits";

    [JsonPropertyName("input_shape")]
    public int[]? InputShape { get; init; }

    [JsonPropertyName("output_shape")]
    public int[]? OutputShape { get; init; }

    [JsonPropertyName("class_names")]
    public string[] ClassNames { get; init; } = Array.Empty<string>();

    [JsonPropertyName("background_id")]
    public int BackgroundId { get; init; }

    [JsonPropertyName("sequence_direction")]
    public string SequenceDirection { get; init; } = "bottom_to_top";

    [JsonPropertyName("seq_len")]
    public int SeqLen { get; init; } = 256;

    [JsonPropertyName("onnx_opset")]
    public int OnnxOpset { get; init; }

    [JsonPropertyName("dynamic_batch")]
    public bool DynamicBatch { get; init; }

    [JsonPropertyName("preprocess")]
    public SequencePreprocessOptions Preprocess { get; init; } = new();

    [JsonPropertyName("postprocess")]
    public SequencePostprocessOptions Postprocess { get; init; } = new();

    public int InputHeight => Preprocess.ResizeHeight > 0
        ? Preprocess.ResizeHeight
        : InputShape is { Length: >= 3 } ? InputShape[2] : 512;

    public int InputWidth => Preprocess.ResizeWidth > 0
        ? Preprocess.ResizeWidth
        : InputShape is { Length: >= 4 } ? InputShape[3] : 256;

    public int OutputClassCount => ClassNames.Length > 0
        ? ClassNames.Length
        : OutputShape is { Length: >= 2 } ? OutputShape[^2] : 0;

    public int OutputSeqLen => SeqLen > 0
        ? SeqLen
        : OutputShape is { Length: >= 1 } ? OutputShape[^1] : 256;

    public int MinSegmentLength => Math.Max(1, Postprocess.RecommendedMinSegmentLength);

    public bool IgnoreBackgroundInFinalLayers => Postprocess.IgnoreBackgroundInFinalLayers;

    public static bool HasConfigForModel(string modelPath)
    {
        return TryLoadFromBundleManifest(modelPath, out _)
               || !string.IsNullOrWhiteSpace(FindSidecarPath(modelPath));
    }

    public static bool HasSidecarForModel(string modelPath)
    {
        return !string.IsNullOrWhiteSpace(FindSidecarPath(modelPath));
    }

    public static bool TryLoadForModel(string modelPath, out SequenceModelMetadata metadata)
    {
        metadata = null!;

        if (TryLoadFromBundleManifest(modelPath, out metadata))
        {
            return true;
        }

        var metaPath = FindSidecarPath(modelPath);
        if (string.IsNullOrWhiteSpace(metaPath) || !File.Exists(metaPath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(metaPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var parsed = JsonSerializer.Deserialize<SequenceModelMetadata>(json, options);
            return TryValidate(parsed, out metadata);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool TryLoadFromBundleManifest(string modelPath, out SequenceModelMetadata metadata)
    {
        metadata = null!;

        var manifestPath = FindBundleManifestPath(modelPath);
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!document.RootElement.TryGetProperty("sequence", out var sequenceElement) ||
                sequenceElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var parsed = sequenceElement.Deserialize<SequenceModelMetadata>(
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            return TryValidate(parsed, out metadata);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool TryValidate(SequenceModelMetadata? parsed, out SequenceModelMetadata metadata)
    {
        metadata = null!;
        if (parsed == null)
        {
            return false;
        }

        if (parsed.InputHeight <= 0 || parsed.InputWidth <= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(parsed.InputName) || string.IsNullOrWhiteSpace(parsed.OutputName))
        {
            return false;
        }

        metadata = parsed;
        return true;
    }

    private static string? FindSidecarPath(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(modelPath);
        var baseName = Path.GetFileNameWithoutExtension(modelPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(baseName))
        {
            return null;
        }

        var exact = Path.Combine(directory, $"{baseName}.meta.json");
        return File.Exists(exact) ? exact : null;
    }

    private static string? FindBundleManifestPath(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(modelPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var manifestPath = Path.Combine(directory, "model.json");
        return File.Exists(manifestPath) ? manifestPath : null;
    }
}

public sealed class SequencePreprocessOptions
{
    [JsonPropertyName("resize_height")]
    public int ResizeHeight { get; set; } = 512;

    [JsonPropertyName("resize_width")]
    public int ResizeWidth { get; set; } = 256;

    [JsonPropertyName("image_preprocess")]
    public string ImagePreprocess { get; set; } = "resize";

    [JsonPropertyName("crop_bottom_ratio")]
    public double CropBottomRatio { get; set; }

    [JsonPropertyName("color_mode")]
    public string ColorMode { get; set; } = "RGB";

    [JsonPropertyName("normalize_mean")]
    public float[] NormalizeMean { get; set; } = new[] { 0.485f, 0.456f, 0.406f };

    [JsonPropertyName("normalize_std")]
    public float[] NormalizeStd { get; set; } = new[] { 0.229f, 0.224f, 0.225f };

    [JsonPropertyName("layout")]
    public string Layout { get; set; } = "NCHW";

    [JsonPropertyName("dtype")]
    public string DType { get; set; } = "float32";
}

public sealed class SequencePostprocessOptions
{
    [JsonPropertyName("output_type")]
    public string OutputType { get; set; } = "logits";

    [JsonPropertyName("argmax_dim")]
    public int ArgmaxDim { get; set; } = 1;

    [JsonPropertyName("recommended_min_segment_length")]
    public int RecommendedMinSegmentLength { get; set; } = 4;

    [JsonPropertyName("ignore_background_in_final_layers")]
    public bool IgnoreBackgroundInFinalLayers { get; set; } = true;
}

public sealed class SequenceOnnxModel : IDisposable
{
    private readonly OrtModelRuntime _runtime;
    private readonly SequenceBandPreprocessor _preprocessor;
    private readonly SequenceBandPostprocessor _postprocessor;
    private bool _fallbackAttempted;

    public SequenceOnnxModel(string modelPath, InferenceDeviceKind deviceKind, SequenceModelMetadata metadata)
    {
        _runtime = new OrtModelRuntime(modelPath, deviceKind, metadata.InputName, metadata.OutputName);
        _preprocessor = new SequenceBandPreprocessor(metadata, _runtime.InputName);
        _postprocessor = new SequenceBandPostprocessor(metadata, _runtime.OutputName);
        ActiveDeviceLabel = _runtime.ActiveDeviceLabel ?? string.Empty;
    }

    public string ActiveDeviceLabel { get; private set; }

    public IReadOnlyList<SequenceBandPrediction> Predict(Mat image)
    {
        if (image == null || image.Empty())
        {
            return Array.Empty<SequenceBandPrediction>();
        }

        var input = _preprocessor.Preprocess(image, out var context);
        using var output = _runtime.Run(input);
        return _postprocessor.Process(output, context);
    }

    public void Warmup(int width = 640, int height = 640)
    {
        using var mat = new Mat(Math.Max(1, height), Math.Max(1, width), MatType.CV_8UC3, Scalar.All(0));
        _ = Predict(mat);
    }

    public bool TryFallbackToCpu(out string message)
    {
        message = string.Empty;
        if (_fallbackAttempted)
        {
            return false;
        }

        _fallbackAttempted = true;
        try
        {
            if (_runtime.TryFallbackToCpu(out message))
            {
                ActiveDeviceLabel = _runtime.ActiveDeviceLabel ?? string.Empty;
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            message = $"Inference fallback failed: {ex.Message}";
            return false;
        }
    }

    public void Dispose()
    {
        _runtime.Dispose();
    }
}

