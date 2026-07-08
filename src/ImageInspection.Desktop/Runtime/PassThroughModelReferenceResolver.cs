namespace VideoInferenceDemo.ImageInspection.Runtime;

public sealed class PassThroughModelReferenceResolver : IModelReferenceResolver
{
    public IReadOnlyList<InspectionModelReference> Resolve(InspectionRecipe recipe)
    {
        return recipe.ModelBindings;
    }
}
