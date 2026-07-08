using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace VideoInferenceDemo;

public sealed class SequenceBandPostprocessor : IModelPostprocessor<SequenceImageTransformContext, IReadOnlyList<SequenceBandPrediction>>
{
    private readonly SequenceModelMetadata _metadata;
    private readonly string _outputName;

    public SequenceBandPostprocessor(SequenceModelMetadata metadata, string outputName)
    {
        _metadata = metadata;
        _outputName = outputName;
    }

    public IReadOnlyList<SequenceBandPrediction> Process(ModelOutput output, SequenceImageTransformContext context)
    {
        var value = output.Outputs.FirstOrDefault(item => string.Equals(item.Name, _outputName, StringComparison.OrdinalIgnoreCase))
                    ?? output.Outputs.FirstOrDefault();
        if (value == null)
        {
            return Array.Empty<SequenceBandPrediction>();
        }

        var logits = value.AsTensor<float>();
        return Postprocess(logits, context.OriginalWidth, context.OriginalHeight, context.Geometry);
    }

    private IReadOnlyList<SequenceBandPrediction> Postprocess(
        Tensor<float> logits,
        int originalWidth,
        int originalHeight,
        SequenceInputGeometry geometry)
    {
        var dims = logits.Dimensions.ToArray();
        if (dims.Length < 3)
        {
            return Array.Empty<SequenceBandPrediction>();
        }

        var classCount = dims[^2];
        var seqLen = dims[^1];
        if (classCount <= 0 || seqLen <= 0)
        {
            return Array.Empty<SequenceBandPrediction>();
        }

        var data = logits.ToArray();
        var labels = new int[seqLen];
        var probabilities = new float[classCount, seqLen];

        for (var seqIdx = 0; seqIdx < seqLen; seqIdx++)
        {
            var maxLogit = float.NegativeInfinity;
            var maxClass = 0;
            for (var classIdx = 0; classIdx < classCount; classIdx++)
            {
                var value = data[(classIdx * seqLen) + seqIdx];
                if (value > maxLogit)
                {
                    maxLogit = value;
                    maxClass = classIdx;
                }
            }

            var denom = 0.0;
            for (var classIdx = 0; classIdx < classCount; classIdx++)
            {
                denom += Math.Exp(data[(classIdx * seqLen) + seqIdx] - maxLogit);
            }

            var safeDenom = Math.Max(1e-9, denom);
            for (var classIdx = 0; classIdx < classCount; classIdx++)
            {
                probabilities[classIdx, seqIdx] = (float)(Math.Exp(data[(classIdx * seqLen) + seqIdx] - maxLogit) / safeDenom);
            }

            labels[seqIdx] = maxClass;
        }

        SmoothShortSegments(labels, _metadata.MinSegmentLength);

        var segments = BuildSegments(labels);
        if (segments.Count == 0)
        {
            return Array.Empty<SequenceBandPrediction>();
        }

        var results = new List<SequenceBandPrediction>(segments.Count);
        var effectiveHeight = geometry.EffectiveHeight;
        var bandHeight = effectiveHeight / (double)Math.Max(1, seqLen);
        foreach (var segment in segments)
        {
            if (_metadata.IgnoreBackgroundInFinalLayers && segment.ClassId == _metadata.BackgroundId)
            {
                continue;
            }

            var confidence = 0f;
            var length = Math.Max(1, segment.End - segment.Start);
            var clampedClassId = Math.Clamp(segment.ClassId, 0, classCount - 1);
            for (var seqIdx = segment.Start; seqIdx < segment.End && seqIdx < seqLen; seqIdx++)
            {
                confidence += probabilities[clampedClassId, seqIdx];
            }

            confidence /= length;

            double y0;
            double y1;
            if (string.Equals(_metadata.SequenceDirection, "bottom_to_top", StringComparison.OrdinalIgnoreCase))
            {
                var yBottom = geometry.SourceTop + effectiveHeight - (segment.Start * bandHeight);
                var yTop = geometry.SourceTop + effectiveHeight - (segment.End * bandHeight);
                y0 = Math.Min(yTop, yBottom);
                y1 = Math.Max(yTop, yBottom);
            }
            else
            {
                y0 = geometry.SourceTop + (segment.Start * bandHeight);
                y1 = geometry.SourceTop + (segment.End * bandHeight);
            }

            var top = ClampToInt(y0, 0, Math.Max(0, originalHeight - 1));
            var bottom = ClampToInt(y1, top + 1, Math.Max(top + 1, originalHeight));
            results.Add(new SequenceBandPrediction(
                segment.ClassId,
                ResolveClassName(segment.ClassId),
                confidence,
                segment.Start,
                segment.End,
                0,
                top,
                Math.Max(1, originalWidth),
                bottom));
        }

        if (ShouldSuppressSingleLowConfidenceC(results))
        {
            return Array.Empty<SequenceBandPrediction>();
        }

        return results;
    }

    private static bool ShouldSuppressSingleLowConfidenceC(IReadOnlyList<SequenceBandPrediction> results)
    {
        return results.Count == 1
               && string.Equals(results[0].ClassName, "C", StringComparison.OrdinalIgnoreCase)
               && results[0].Confidence < 0.90f;
    }

    private static void SmoothShortSegments(int[] labels, int minSegmentLength)
    {
        if (labels.Length == 0 || minSegmentLength <= 1)
        {
            return;
        }

        while (true)
        {
            var segments = BuildSegments(labels);
            if (segments.Count <= 1)
            {
                return;
            }

            var changed = false;
            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                if (segment.End - segment.Start >= minSegmentLength)
                {
                    continue;
                }

                var replacement = segment.ClassId;
                if (i == 0)
                {
                    replacement = segments[i + 1].ClassId;
                }
                else if (i == segments.Count - 1)
                {
                    replacement = segments[i - 1].ClassId;
                }
                else
                {
                    var left = segments[i - 1];
                    var right = segments[i + 1];
                    if (left.ClassId == right.ClassId)
                    {
                        replacement = left.ClassId;
                    }
                    else
                    {
                        var leftLength = left.End - left.Start;
                        var rightLength = right.End - right.Start;
                        replacement = leftLength >= rightLength ? left.ClassId : right.ClassId;
                    }
                }

                Array.Fill(labels, replacement, segment.Start, segment.End - segment.Start);
                changed = true;
                break;
            }

            if (!changed)
            {
                return;
            }
        }
    }

    private static List<SequenceSegment> BuildSegments(int[] labels)
    {
        var segments = new List<SequenceSegment>();
        if (labels.Length == 0)
        {
            return segments;
        }

        var start = 0;
        var current = labels[0];
        for (var i = 1; i < labels.Length; i++)
        {
            if (labels[i] == current)
            {
                continue;
            }

            segments.Add(new SequenceSegment(current, start, i));
            current = labels[i];
            start = i;
        }

        segments.Add(new SequenceSegment(current, start, labels.Length));
        return segments;
    }

    private string ResolveClassName(int classId)
    {
        if (_metadata.ClassNames.Length == 0 || classId < 0 || classId >= _metadata.ClassNames.Length)
        {
            return $"#{classId}";
        }

        return _metadata.ClassNames[classId];
    }

    private static int ClampToInt(double value, int min, int max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return min;
        }

        var rounded = (int)Math.Round(value);
        if (rounded < min)
        {
            return min;
        }

        return rounded > max ? max : rounded;
    }

    private readonly record struct SequenceSegment(int ClassId, int Start, int End);
}
