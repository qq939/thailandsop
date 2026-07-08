using System;
using System.Linq;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace VideoInferenceDemo;

public sealed class TcnPredictionPostprocessor : IModelPostprocessor<TcnFeatureFrame, TcnPrediction?>
{
    private readonly string _outputName;
    private readonly bool _applySoftmax;
    private readonly string[]? _classNames;

    public TcnPredictionPostprocessor(string outputName, bool applySoftmax, string[]? classNames)
    {
        _outputName = outputName;
        _applySoftmax = applySoftmax;
        _classNames = classNames;
    }

    public TcnPrediction? Process(ModelOutput output, TcnFeatureFrame context)
    {
        var value = output.Outputs.FirstOrDefault(item => string.Equals(item.Name, _outputName, StringComparison.OrdinalIgnoreCase))
                    ?? output.Outputs.FirstOrDefault();
        if (value == null)
        {
            return null;
        }

        var logits = value.AsTensor<float>();
        if (logits.Length == 0)
        {
            return null;
        }

        var (classId, score) = ArgMax(logits, _applySoftmax);
        var label = ResolveLabel(classId);
        return new TcnPrediction(
            context.RunUuid,
            context.RunStartedUtcMs,
            label,
            score,
            classId,
            context.SourceKey,
            context.FrameIndex,
            context.PtsMs);
    }

    private (int classId, float score) ArgMax(Tensor<float> tensor, bool applySoftmax)
    {
        var data = tensor.ToArray();
        var count = data.Length;
        var maxIdx = 0;
        var maxVal = data[0];
        for (var i = 1; i < count; i++)
        {
            var val = data[i];
            if (val > maxVal)
            {
                maxVal = val;
                maxIdx = i;
            }
        }

        if (!applySoftmax)
        {
            return (maxIdx, maxVal);
        }

        var denom = 0.0;
        for (var i = 0; i < count; i++)
        {
            denom += Math.Exp(data[i] - maxVal);
        }

        var prob = (float)(1.0 / Math.Max(1e-9, denom));
        return (maxIdx, prob);
    }

    private string ResolveLabel(int classId)
    {
        if (_classNames == null || _classNames.Length == 0)
        {
            return $"#{classId}";
        }

        if (classId < 0 || classId >= _classNames.Length)
        {
            return $"#{classId}";
        }

        return _classNames[classId];
    }
}
