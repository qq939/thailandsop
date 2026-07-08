namespace VideoInferenceDemo;

public interface IModelRuntime : IDisposable
{
    string? ActiveDeviceLabel { get; }
    ModelOutput Run(ModelInput input);
    bool TryFallbackToCpu(out string message);
}
