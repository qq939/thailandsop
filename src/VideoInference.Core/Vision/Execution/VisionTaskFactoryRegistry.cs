namespace VideoInferenceDemo;

public sealed class VisionTaskFactoryRegistry
{
    private readonly IReadOnlyList<IVisionTaskFactory> _factories;

    public VisionTaskFactoryRegistry(IEnumerable<IVisionTaskFactory> factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        _factories = factories.ToArray();
    }

    public IVisionTask Create(VisionTaskDefinition definition, VisionTaskCreationContext context)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var factory = _factories.FirstOrDefault(item => item.CanCreate(definition));
        if (factory == null)
        {
            throw new NotSupportedException(
                $"No vision task factory can create task '{definition.Id}' ({definition.TaskKind}, {definition.RuntimeKind}).");
        }

        return factory.Create(definition, context);
    }
}
