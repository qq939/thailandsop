namespace VideoInferenceDemo;

public sealed class PrimaryVisionTaskBinding
{
    private static readonly VisionTaskFactoryRegistry OnnxTaskFactoryRegistry = new(new IVisionTaskFactory[]
    {
        OnnxVisionTaskFactory.Instance
    });

    public PrimaryVisionTaskBinding(
        VisionTaskDefinition definition,
        VisionTaskFactoryRegistry registry,
        VisionTaskCreationContext context,
        ModelBindingPlan? modelBindingPlan = null)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        ModelBindingPlan = modelBindingPlan;
    }

    public VisionTaskDefinition Definition { get; }
    public VisionTaskFactoryRegistry Registry { get; }
    public VisionTaskCreationContext Context { get; }
    public ModelBindingPlan? ModelBindingPlan { get; }

    public static PrimaryVisionTaskBinding ForTask(
        VisionTaskDefinition definition,
        VisionTaskFactoryRegistry registry,
        VisionTaskCreationContext context)
    {
        return new PrimaryVisionTaskBinding(definition, registry, context);
    }

    public static PrimaryVisionTaskBinding ForModel(
        ModelBindingPlan modelBindingPlan,
        VisionTaskCreationContext context)
    {
        ArgumentNullException.ThrowIfNull(modelBindingPlan);

        return new PrimaryVisionTaskBinding(
            ModelCatalogVisionTaskMapper.ToVisionTaskDefinition(modelBindingPlan.Model),
            OnnxTaskFactoryRegistry,
            context,
            modelBindingPlan);
    }
}
