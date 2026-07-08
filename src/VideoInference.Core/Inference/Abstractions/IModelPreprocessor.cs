using OpenCvSharp;

namespace VideoInferenceDemo;

public interface IModelPreprocessor<in TInput, TContext>
{
    ModelInput Preprocess(TInput input, out TContext context);
}
