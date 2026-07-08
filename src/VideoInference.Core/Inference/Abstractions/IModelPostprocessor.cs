namespace VideoInferenceDemo;

public interface IModelPostprocessor<TContext, TResult>
{
    TResult Process(ModelOutput output, TContext context);
}
