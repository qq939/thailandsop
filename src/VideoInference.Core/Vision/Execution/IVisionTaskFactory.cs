namespace VideoInferenceDemo;

public interface IVisionTaskFactory
{
    bool CanCreate(VisionTaskDefinition definition);
    IVisionTask Create(VisionTaskDefinition definition, VisionTaskCreationContext context);
}
