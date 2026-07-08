using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace VideoInferenceDemo;

public sealed class YoloObbDetectionPostprocessor : IModelPostprocessor<YoloImageTransformContext, YoloObbDetection[]>
{
    private readonly string _outputName;
    private readonly float _confidenceThreshold;
    private readonly float _nmsThreshold;
    private readonly string[]? _classNames;
    private readonly YoloObbDetectionMetadata? _metadata;

    public YoloObbDetectionPostprocessor(
        string outputName,
        float confidenceThreshold,
        float nmsThreshold,
        string[]? classNames,
        YoloObbDetectionMetadata? metadata = null)
    {
        _outputName = outputName;
        _confidenceThreshold = confidenceThreshold;
        _nmsThreshold = nmsThreshold;
        _classNames = classNames;
        _metadata = metadata;
    }

    public YoloObbDetection[] Process(ModelOutput output, YoloImageTransformContext context)
    {
        var value = output.Outputs.FirstOrDefault(item => string.Equals(item.Name, _outputName, StringComparison.OrdinalIgnoreCase))
                    ?? output.Outputs.FirstOrDefault();
        if (value == null)
        {
            return Array.Empty<YoloObbDetection>();
        }

        return ExtractDetections(value.AsTensor<float>(), context).ToArray();
    }

    private IEnumerable<YoloObbDetection> ExtractDetections(Tensor<float> output, YoloImageTransformContext context)
    {
        var shape = output.Dimensions.ToArray();
        if (shape.Length != 3)
        {
            throw new NotSupportedException($"Expected a 3D OBB output tensor, but got [{string.Join(", ", shape)}].");
        }

        var channelsFirst = ResolveChannelsFirst(shape);
        var boxCount = channelsFirst ? shape[2] : shape[1];
        var featureCount = channelsFirst ? shape[1] : shape[2];
        var hasObjectness = ResolveObjectnessMode(channelsFirst);
        var classCount = featureCount - (hasObjectness ? 6 : 5);
        if (_metadata?.ClassCount is > 0 and var configuredClassCount && configuredClassCount != classCount)
        {
            classCount = configuredClassCount;
        }

        if (classCount <= 0)
        {
            throw new NotSupportedException(
                $"The output tensor shape [{string.Join(", ", shape)}] does not look like a YOLO-OBB detection head.");
        }

        var candidates = new List<YoloObbDetection>(boxCount);
        for (var boxIndex = 0; boxIndex < boxCount; boxIndex++)
        {
            var centerX = ValueAt(output, channelsFirst, boxIndex, 0);
            var centerY = ValueAt(output, channelsFirst, boxIndex, 1);
            var width = ValueAt(output, channelsFirst, boxIndex, 2);
            var height = ValueAt(output, channelsFirst, boxIndex, 3);

            var classStart = 4;
            var objectness = 1f;
            if (hasObjectness)
            {
                objectness = Sigmoid(ValueAt(output, channelsFirst, boxIndex, 4));
                classStart = 5;
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

            var angleRaw = ValueAt(output, channelsFirst, boxIndex, classStart + classCount);
            var adjustedCenterX = (centerX - context.Geometry.PadX) / context.Geometry.RatioX;
            var adjustedCenterY = (centerY - context.Geometry.PadY) / context.Geometry.RatioY;
            var adjustedWidth = width / context.Geometry.RatioX;
            var adjustedHeight = height / context.Geometry.RatioY;
            if (adjustedWidth < 1f || adjustedHeight < 1f)
            {
                continue;
            }

            candidates.Add(new YoloObbDetection(
                bestClassId,
                ResolveClassName(bestClassId),
                score,
                Clamp(adjustedCenterX, 0, context.Geometry.OriginalWidth),
                Clamp(adjustedCenterY, 0, context.Geometry.OriginalHeight),
                MathF.Max(1f, adjustedWidth),
                MathF.Max(1f, adjustedHeight),
                NormalizeAngleDeg(ToDegreesIfNeeded(angleRaw))));
        }

        return ApplyNms(candidates, _nmsThreshold);
    }

    private IEnumerable<YoloObbDetection> ApplyNms(List<YoloObbDetection> detections, float iouThreshold)
    {
        if (detections.Count <= 1)
        {
            return detections;
        }

        var candidates = detections
            .Select(item => new RectDetectionCandidate(
                item.ClassId,
                item.Score,
                item.X1,
                item.Y1,
                item.Width,
                item.Height))
            .ToList();

        var kept = RectDetectionNms.Run(candidates, iouThreshold);
        return kept.Select(candidate =>
        {
            var original = detections.First(item =>
                item.ClassId == candidate.ClassId &&
                Math.Abs(item.Score - candidate.Score) < 0.0001f &&
                Math.Abs(item.X1 - candidate.X) < 0.5f &&
                Math.Abs(item.Y1 - candidate.Y) < 0.5f);
            return original;
        });
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
            _ => shape[1] <= 256 && shape[2] > shape[1]
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

    private static float ValueAt(Tensor<float> output, bool channelsFirst, int boxIndex, int featureIndex)
    {
        return channelsFirst
            ? output[0, featureIndex, boxIndex]
            : output[0, boxIndex, featureIndex];
    }

    private static float Sigmoid(float value) => 1f / (1f + MathF.Exp(-value));

    private static float ToDegreesIfNeeded(float angle)
    {
        return MathF.Abs(angle) <= MathF.PI * 2f
            ? angle * 180f / MathF.PI
            : angle;
    }

    private static float NormalizeAngleDeg(float angle)
    {
        while (angle <= -180f)
        {
            angle += 360f;
        }

        while (angle > 180f)
        {
            angle -= 360f;
        }

        return angle;
    }

    private static float Clamp(float value, float min, float max)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return min;
        }

        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }
}
