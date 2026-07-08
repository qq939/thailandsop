using System;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace VideoInferenceDemo;

public sealed class TcnFeatureWindowPreprocessor : IModelPreprocessor<TcnInferenceRequest, TcnFeatureFrame>
{
    private readonly string _inputName;
    private readonly int _featureDim;
    private readonly int _windowSize;
    private readonly float[] _inputBuffer;

    public TcnFeatureWindowPreprocessor(string inputName, int featureDim, int windowSize)
    {
        _inputName = inputName;
        _featureDim = Math.Max(1, featureDim);
        _windowSize = Math.Max(1, windowSize);
        _inputBuffer = new float[_featureDim * _windowSize];
    }

    public ModelInput Preprocess(TcnInferenceRequest input, out TcnFeatureFrame context)
    {
        context = input.Frame;
        Array.Clear(_inputBuffer, 0, _inputBuffer.Length);

        var start = input.WindowCount < _windowSize ? 0 : input.WindowIndex;
        for (var t = 0; t < _windowSize; t++)
        {
            var src = input.Window[(start + t) % _windowSize];
            var baseIdx = t;
            for (var c = 0; c < _featureDim; c++)
            {
                _inputBuffer[c * _windowSize + baseIdx] = src[c];
            }
        }

        var tensor = new DenseTensor<float>(_inputBuffer, new[] { 1, _featureDim, _windowSize });
        return new ModelInput(new[] { NamedOnnxValue.CreateFromTensor(_inputName, tensor) });
    }
}

public sealed class TcnInferenceRequest
{
    public TcnInferenceRequest(TcnFeatureFrame frame, float[][] window, int windowCount, int windowIndex)
    {
        Frame = frame;
        Window = window ?? throw new ArgumentNullException(nameof(window));
        WindowCount = windowCount;
        WindowIndex = windowIndex;
    }

    public TcnFeatureFrame Frame { get; }
    public float[][] Window { get; }
    public int WindowCount { get; }
    public int WindowIndex { get; }
}
