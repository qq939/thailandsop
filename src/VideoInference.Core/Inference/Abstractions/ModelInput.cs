using System;
using System.Collections.Generic;
using Microsoft.ML.OnnxRuntime;

namespace VideoInferenceDemo;

public sealed class ModelInput
{
    public ModelInput(IReadOnlyList<NamedOnnxValue> inputs)
    {
        Inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
    }

    public IReadOnlyList<NamedOnnxValue> Inputs { get; }
}

public sealed class ModelOutput : IDisposable
{
    public ModelOutput(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs)
    {
        Outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
    }

    public IDisposableReadOnlyCollection<DisposableNamedOnnxValue> Outputs { get; }

    public void Dispose()
    {
        Outputs.Dispose();
    }
}
