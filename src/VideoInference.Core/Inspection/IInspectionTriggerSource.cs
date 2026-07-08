namespace VideoInferenceDemo;

public interface IInspectionTriggerSource
{
    event EventHandler<InspectionTriggerEventArgs>? Triggered;
}
