namespace VideoInferenceDemo;

public interface IInspectionAction
{
    InspectionCycleResult Execute(InspectionRequest request);
}
