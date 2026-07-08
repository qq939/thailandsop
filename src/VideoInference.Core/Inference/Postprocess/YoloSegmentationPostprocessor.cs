using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace VideoInferenceDemo;

public sealed class YoloSegmentationPostprocessor : IModelPostprocessor<YoloImageTransformContext, YoloSegmentation[]>
{
    private readonly string _defaultOutputName;
    private readonly float _confidenceThreshold;
    private readonly float _nmsThreshold;
    private readonly YoloSegmentationMetadata? _metadata;
    private string[]? _classNames;

    public YoloSegmentationPostprocessor(
        string defaultOutputName,
        float confidenceThreshold,
        float nmsThreshold,
        string[]? classNames,
        YoloSegmentationMetadata? metadata = null)
    {
        _defaultOutputName = defaultOutputName;
        _confidenceThreshold = confidenceThreshold;
        _nmsThreshold = nmsThreshold;
        _classNames = classNames;
        _metadata = metadata;
    }

    public void UpdateClasses(string[]? classNames)
    {
        _classNames = classNames;
    }

    public YoloSegmentation[] Process(ModelOutput output, YoloImageTransformContext context)
    {
        if (!TryResolveOutputs(output.Outputs, out var detectionTensor, out var prototypeTensor))
        {
            return Array.Empty<YoloSegmentation>();
        }

        var candidates = ExtractCandidates(detectionTensor, context);
        if (candidates.Count == 0)
        {
            return Array.Empty<YoloSegmentation>();
        }

        var kept = ApplyNms(candidates, _nmsThreshold);
        var results = new List<YoloSegmentation>(kept.Count);
        foreach (var candidate in kept)
        {
            var mask = BuildMask(candidate, prototypeTensor, context.Geometry);
            results.Add(new YoloSegmentation(
                candidate.ClassId,
                ResolveClassName(candidate.ClassId),
                candidate.Score,
                candidate.X1,
                candidate.Y1,
                candidate.X2,
                candidate.Y2,
                mask,
                context.Geometry.OriginalWidth,
                context.Geometry.OriginalHeight));
        }

        return results.ToArray();
    }

    private bool TryResolveOutputs(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs,
        out Tensor<float> detectionTensor,
        out Tensor<float> prototypeTensor)
    {
        detectionTensor = default!;
        prototypeTensor = default!;

        var named = outputs.ToArray();
        DisposableNamedOnnxValue? detection = null;
        DisposableNamedOnnxValue? prototype = null;

        if (!string.IsNullOrWhiteSpace(_metadata?.DetectionOutputName))
        {
            detection = named.FirstOrDefault(item => string.Equals(item.Name, _metadata.DetectionOutputName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(_metadata?.PrototypeOutputName))
        {
            prototype = named.FirstOrDefault(item => string.Equals(item.Name, _metadata.PrototypeOutputName, StringComparison.OrdinalIgnoreCase));
        }

        detection ??= named.FirstOrDefault(item => string.Equals(item.Name, _defaultOutputName, StringComparison.OrdinalIgnoreCase));

        foreach (var output in named)
        {
            if (output.Value is not Tensor<float> tensor)
            {
                continue;
            }

            var dims = tensor.Dimensions.ToArray();
            if (prototype == null && LooksLikePrototype(dims))
            {
                prototype = output;
                continue;
            }

            if (detection == null && LooksLikeDetectionHead(dims))
            {
                detection = output;
            }
        }

        if (detection?.Value is not Tensor<float> resolvedDetection ||
            prototype?.Value is not Tensor<float> resolvedPrototype)
        {
            return false;
        }

        detectionTensor = resolvedDetection;
        prototypeTensor = resolvedPrototype;
        return true;
    }

    private List<SegmentationCandidate> ExtractCandidates(Tensor<float> output, YoloImageTransformContext context)
    {
        var shape = output.Dimensions.ToArray();
        if (shape.Length != 3)
        {
            throw new NotSupportedException($"Expected a 3D YOLO segmentation detection output tensor, but got [{string.Join(", ", shape)}].");
        }

        var channelsFirst = ResolveChannelsFirst(shape);
        var boxCount = channelsFirst ? shape[2] : shape[1];
        var featureCount = channelsFirst ? shape[1] : shape[2];
        var hasObjectness = ResolveObjectnessMode(channelsFirst);
        var classStart = hasObjectness ? 5 : 4;
        var classCount = ResolveClassCount(featureCount, classStart);
        var maskCoefficientCount = featureCount - classStart - classCount;
        if (classCount <= 0 || maskCoefficientCount <= 0)
        {
            throw new NotSupportedException(
                $"The output tensor shape [{string.Join(", ", shape)}] does not look like a YOLO segmentation head.");
        }

        var candidates = new List<SegmentationCandidate>(boxCount);
        for (var boxIndex = 0; boxIndex < boxCount; boxIndex++)
        {
            var centerX = ValueAt(output, channelsFirst, boxIndex, 0);
            var centerY = ValueAt(output, channelsFirst, boxIndex, 1);
            var width = ValueAt(output, channelsFirst, boxIndex, 2);
            var height = ValueAt(output, channelsFirst, boxIndex, 3);

            var objectness = 1f;
            if (hasObjectness)
            {
                objectness = Sigmoid(ValueAt(output, channelsFirst, boxIndex, 4));
            }

            var bestClassId = -1;
            var bestClassScore = 0f;
            for (var classId = 0; classId < classCount; classId++)
            {
                var rawClassScore = ValueAt(output, channelsFirst, boxIndex, classStart + classId);
                var classScore = hasObjectness ? Sigmoid(rawClassScore) : rawClassScore;
                if (classScore <= bestClassScore)
                {
                    continue;
                }

                bestClassScore = classScore;
                bestClassId = classId;
            }

            if (bestClassId < 0)
            {
                continue;
            }

            var score = hasObjectness ? objectness * bestClassScore : bestClassScore;
            if (score < _confidenceThreshold)
            {
                continue;
            }

            var x1 = centerX - (width / 2f);
            var y1 = centerY - (height / 2f);
            var adjusted = context.Geometry.AdjustRect(new ImageRectF(x1, y1, width, height));
            if (adjusted.Width < 1f || adjusted.Height < 1f)
            {
                continue;
            }

            var coefficients = new float[maskCoefficientCount];
            var coefficientStart = classStart + classCount;
            for (var i = 0; i < maskCoefficientCount; i++)
            {
                coefficients[i] = ValueAt(output, channelsFirst, boxIndex, coefficientStart + i);
            }

            candidates.Add(new SegmentationCandidate(
                bestClassId,
                score,
                adjusted.X,
                adjusted.Y,
                adjusted.Right,
                adjusted.Bottom,
                coefficients));
        }

        return candidates;
    }

    private byte[] BuildMask(SegmentationCandidate candidate, Tensor<float> prototype, ImageAdjustmentGeometry geometry)
    {
        var dims = prototype.Dimensions.ToArray();
        if (dims.Length != 4)
        {
            return Array.Empty<byte>();
        }

        var channelsFirst = dims[1] <= 256;
        var maskChannels = channelsFirst ? dims[1] : dims[3];
        var maskHeight = channelsFirst ? dims[2] : dims[1];
        var maskWidth = channelsFirst ? dims[3] : dims[2];
        if (maskChannels <= 0 || maskHeight <= 0 || maskWidth <= 0 || candidate.MaskCoefficients.Length == 0)
        {
            return Array.Empty<byte>();
        }

        var targetWidth = geometry.OriginalWidth;
        var targetHeight = geometry.OriginalHeight;
        var mask = new byte[targetWidth * targetHeight];

        var minX = ClampToInt(candidate.X1, 0, targetWidth - 1);
        var maxX = ClampToInt(candidate.X2, 0, targetWidth);
        var minY = ClampToInt(candidate.Y1, 0, targetHeight - 1);
        var maxY = ClampToInt(candidate.Y2, 0, targetHeight);
        if (maxX <= minX || maxY <= minY)
        {
            return mask;
        }

        var protoMinX = ClampToIntFloor(ToPrototypeX(minX, geometry, maskWidth), 0, maskWidth - 1);
        var protoMaxX = ClampToIntCeiling(ToPrototypeX(maxX, geometry, maskWidth), protoMinX + 1, maskWidth);
        var protoMinY = ClampToIntFloor(ToPrototypeY(minY, geometry, maskHeight), 0, maskHeight - 1);
        var protoMaxY = ClampToIntCeiling(ToPrototypeY(maxY, geometry, maskHeight), protoMinY + 1, maskHeight);
        var protoRoiWidth = protoMaxX - protoMinX;
        var protoRoiHeight = protoMaxY - protoMinY;
        if (protoRoiWidth <= 0 || protoRoiHeight <= 0)
        {
            return mask;
        }

        var protoMask = BuildPrototypeMask(
            candidate,
            prototype,
            channelsFirst,
            maskChannels,
            maskHeight,
            maskWidth,
            protoMinX,
            protoMinY,
            protoRoiWidth,
            protoRoiHeight);
        ResizeMaskIntoTarget(mask, targetWidth, minX, minY, maxX - minX, maxY - minY, protoMask, protoRoiWidth, protoRoiHeight);
        return mask;
    }

    private byte[] BuildPrototypeMask(
        SegmentationCandidate candidate,
        Tensor<float> prototype,
        bool channelsFirst,
        int maskChannels,
        int maskHeight,
        int maskWidth,
        int protoMinX,
        int protoMinY,
        int protoRoiWidth,
        int protoRoiHeight)
    {
        var coefficientCount = Math.Min(maskChannels, candidate.MaskCoefficients.Length);
        var protoMask = new byte[protoRoiWidth * protoRoiHeight];
        var threshold = _metadata?.MaskThreshold ?? 0.5f;
        var denseValues = prototype is DenseTensor<float> denseTensor
            ? denseTensor.Buffer.Span
            : ReadOnlySpan<float>.Empty;

        for (var localY = 0; localY < protoRoiHeight; localY++)
        {
            var protoY = protoMinY + localY;
            var rowOffset = localY * protoRoiWidth;
            for (var localX = 0; localX < protoRoiWidth; localX++)
            {
                var protoX = protoMinX + localX;
                var value = 0f;
                for (var channel = 0; channel < coefficientCount; channel++)
                {
                    var prototypeValue = denseValues.IsEmpty
                        ? PrototypeAt(prototype, channelsFirst, channel, protoY, protoX)
                        : PrototypeAt(denseValues, channelsFirst, channel, protoY, protoX, maskChannels, maskHeight, maskWidth);
                    value += candidate.MaskCoefficients[channel] * prototypeValue;
                }

                if (Sigmoid(value) >= threshold)
                {
                    protoMask[rowOffset + localX] = 255;
                }
            }
        }

        return protoMask;
    }

    private static void ResizeMaskIntoTarget(
        byte[] targetMask,
        int targetWidth,
        int targetX,
        int targetY,
        int targetRoiWidth,
        int targetRoiHeight,
        byte[] protoMask,
        int protoRoiWidth,
        int protoRoiHeight)
    {
        if (targetRoiWidth <= 0 || targetRoiHeight <= 0 || protoRoiWidth <= 0 || protoRoiHeight <= 0)
        {
            return;
        }

        using var protoMat = Mat.FromPixelData(protoRoiHeight, protoRoiWidth, MatType.CV_8UC1, protoMask);
        using var resized = new Mat();
        Cv2.Resize(protoMat, resized, new Size(targetRoiWidth, targetRoiHeight), 0, 0, InterpolationFlags.Nearest);

        for (var row = 0; row < targetRoiHeight; row++)
        {
            Marshal.Copy(resized.Ptr(row), targetMask, ((targetY + row) * targetWidth) + targetX, targetRoiWidth);
        }
    }

    private List<SegmentationCandidate> ApplyNms(List<SegmentationCandidate> detections, float iouThreshold)
    {
        if (detections.Count <= 1)
        {
            return detections;
        }

        var byKey = new Dictionary<(int ClassId, float X, float Y, float Width, float Height, float Score), SegmentationCandidate>();
        var candidates = new List<RectDetectionCandidate>(detections.Count);
        foreach (var item in detections)
        {
            var key = (
                ClassId: item.ClassId,
                X: item.X1,
                Y: item.Y1,
                Width: item.X2 - item.X1,
                Height: item.Y2 - item.Y1,
                Score: item.Score);
            byKey[key] = item;
            candidates.Add(new RectDetectionCandidate(key.ClassId, key.Score, key.X, key.Y, key.Width, key.Height));
        }

        var kept = RectDetectionNms.Run(candidates, iouThreshold);
        var results = new List<SegmentationCandidate>(kept.Count);
        foreach (var item in kept)
        {
            var key = (item.ClassId, item.X, item.Y, item.Width, item.Height, item.Score);
            if (byKey.TryGetValue(key, out var candidate))
            {
                results.Add(candidate);
            }
        }

        return results;
    }

    private int ResolveClassCount(int featureCount, int classStart)
    {
        if (_metadata?.ClassCount is > 0 and var configuredClassCount &&
            configuredClassCount < featureCount - classStart)
        {
            return configuredClassCount;
        }

        if (_classNames is { Length: > 0 } && _classNames.Length < featureCount - classStart)
        {
            return _classNames.Length;
        }

        var likelyMaskCoefficients = 32;
        var inferred = featureCount - classStart - likelyMaskCoefficients;
        return inferred > 0 ? inferred : featureCount - classStart;
    }

    private string ResolveClassName(int classId)
    {
        if (_classNames is { Length: > 0 } &&
            classId >= 0 &&
            classId < _classNames.Length &&
            !string.IsNullOrWhiteSpace(_classNames[classId]))
        {
            return _classNames[classId];
        }

        return $"#{classId}";
    }

    private bool ResolveChannelsFirst(int[] shape)
    {
        return _metadata?.OutputLayout switch
        {
            YoloOutputLayout.ChannelsFirst => true,
            YoloOutputLayout.BoxesFirst => false,
            _ => shape[1] <= 512 && shape[2] > shape[1]
        };
    }

    private bool ResolveObjectnessMode(bool channelsFirst)
    {
        return _metadata?.ScoreMode switch
        {
            YoloScoreMode.ClassOnly => false,
            YoloScoreMode.ObjectnessAndClass => true,
            _ => !channelsFirst
        };
    }

    private static bool LooksLikePrototype(int[] dims)
    {
        return dims.Length == 4 &&
               dims[0] == 1 &&
               ((dims[1] <= 256 && dims[2] > 1 && dims[3] > 1) ||
                (dims[3] <= 256 && dims[1] > 1 && dims[2] > 1));
    }

    private static bool LooksLikeDetectionHead(int[] dims)
    {
        return dims.Length == 3 && dims[0] == 1;
    }

    private static float ValueAt(Tensor<float> output, bool channelsFirst, int boxIndex, int featureIndex)
    {
        return channelsFirst
            ? output[0, featureIndex, boxIndex]
            : output[0, boxIndex, featureIndex];
    }

    private static float PrototypeAt(Tensor<float> prototype, bool channelsFirst, int channel, int y, int x)
    {
        return channelsFirst
            ? prototype[0, channel, y, x]
            : prototype[0, y, x, channel];
    }

    private static float PrototypeAt(
        ReadOnlySpan<float> prototype,
        bool channelsFirst,
        int channel,
        int y,
        int x,
        int channels,
        int height,
        int width)
    {
        var offset = channelsFirst
            ? ((channel * height) + y) * width + x
            : ((y * width) + x) * channels + channel;
        return prototype[offset];
    }

    private static float ToPrototypeX(int x, ImageAdjustmentGeometry geometry, int maskWidth)
    {
        var modelX = (x * geometry.RatioX) + geometry.PadX;
        return modelX / Math.Max(1, geometry.TargetWidth) * maskWidth;
    }

    private static float ToPrototypeY(int y, ImageAdjustmentGeometry geometry, int maskHeight)
    {
        var modelY = (y * geometry.RatioY) + geometry.PadY;
        return modelY / Math.Max(1, geometry.TargetHeight) * maskHeight;
    }

    private static int ClampToIntFloor(float value, int min, int max)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return min;
        }

        var rounded = (int)MathF.Floor(value);
        if (rounded < min)
        {
            return min;
        }

        return rounded > max ? max : rounded;
    }

    private static int ClampToIntCeiling(float value, int min, int max)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return min;
        }

        var rounded = (int)MathF.Ceiling(value);
        if (rounded < min)
        {
            return min;
        }

        return rounded > max ? max : rounded;
    }

    private static int ClampToInt(float value, int min, int max)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return min;
        }

        var rounded = (int)MathF.Round(value);
        if (rounded < min)
        {
            return min;
        }

        return rounded > max ? max : rounded;
    }

    private static float Sigmoid(float value) => 1f / (1f + MathF.Exp(-value));

    private sealed record SegmentationCandidate(
        int ClassId,
        float Score,
        float X1,
        float Y1,
        float X2,
        float Y2,
        float[] MaskCoefficients);
}
