namespace VideoInferenceDemo;

public interface IInspectionResultStore
{
    Task SaveAsync(InspectionCycleResult result, CancellationToken cancellationToken = default);
}
