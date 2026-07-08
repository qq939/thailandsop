using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace VideoInferenceDemo;

public sealed class YoloDetectionPostprocessor : IModelPostprocessor<YoloImageTransformContext, YoloDetection[]>
{
    private readonly string _outputName;
    private readonly float _confidenceThreshold;
    private readonly float _nmsThreshold;
    private readonly string[]? _classNames;
    private readonly YoloDetectionMetadata? _metadata;

    public YoloDetectionPostprocessor(string outputName, float confidenceThreshold, float nmsThreshold, string[]? classNames, YoloDetectionMetadata? metadata = null)
    {
        _outputName = outputName;
        _confidenceThreshold = confidenceThreshold;
        _nmsThreshold = nmsThreshold;
        _classNames = classNames;
        _metadata = metadata;
    }

    public YoloDetection[] Process(ModelOutput output, YoloImageTransformContext context)
    {
        var value = output.Outputs.FirstOrDefault(item => string.Equals(item.Name, _outputName, StringComparison.OrdinalIgnoreCase))
                    ?? output.Outputs.FirstOrDefault();
        if (value == null)
        {
            return Array.Empty<YoloDetection>();
        }

        var outputTensor = value.AsTensor<float>();
        return ExtractDetections(outputTensor, context).ToArray();
    }

    private IEnumerable<YoloDetection> ExtractDetections(Tensor<float> output, YoloImageTransformContext context)
    {
        var shape = output.Dimensions.ToArray();
        if (shape.Length != 3)
        {
            throw new NotSupportedException($"Expected a 3D detection output tensor, but got [{string.Join(", ", shape)}].");
        }

        var channelsFirst = ResolveChannelsFirst(shape);
        var boxCount = channelsFirst ? shape[2] : shape[1];
        var featureCount = channelsFirst ? shape[1] : shape[2];
        var hasObjectness = ResolveObjectnessMode(channelsFirst);
        var classCount = featureCount - (hasObjectness ? 5 : 4);
        if (_metadata?.ClassCount is > 0 and var configuredClassCount && configuredClassCount != classCount)
        {
            classCount = configuredClassCount;
        }
        if (classCount <= 0)
        {
            throw new NotSupportedException(
                $"The output tensor shape [{string.Join(", ", shape)}] does not look like a YOLO detection head.");
        }

        var candidates = new List<YoloDetection>(boxCount);
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

            var x1 = centerX - (width / 2f);
            var y1 = centerY - (height / 2f);
            var x2 = centerX + (width / 2f);
            var y2 = centerY + (height / 2f);

            var adjusted = context.Geometry.AdjustRect(new ImageRectF(x1, y1, width, height));
            x1 = adjusted.X;
            y1 = adjusted.Y;
            x2 = adjusted.Right;
            y2 = adjusted.Bottom;

            if (x2 - x1 < 1f || y2 - y1 < 1f)
            {
                continue;
            }

            candidates.Add(new YoloDetection(
                bestClassId,
                ResolveClassName(bestClassId),
                score,
                x1,
                y1,
                x2,
                y2));
        }

        return ApplyNms(candidates, _nmsThreshold);
    }

    private IEnumerable<YoloDetection> ApplyNms(List<YoloDetection> detections, float iouThreshold)
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
            new YoloDetection(
                candidate.ClassId,
                ResolveClassName(candidate.ClassId),
                candidate.Score,
                candidate.X,
                candidate.Y,
                candidate.X + candidate.Width,
                candidate.Y + candidate.Height));
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

    private static float ValueAt(Tensor<float> output, bool channelsFirst, int boxIndex, int featureIndex)
    {
        return channelsFirst
            ? output[0, featureIndex, boxIndex]
            : output[0, boxIndex, featureIndex];
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

    private static float Sigmoid(float value) => 1f / (1f + MathF.Exp(-value));

}
