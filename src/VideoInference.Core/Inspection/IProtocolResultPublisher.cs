namespace VideoInferenceDemo;

public interface IProtocolResultPublisher
{
    Task PublishAsync(InspectionCycleResult result, CancellationToken cancellationToken = default);
}
