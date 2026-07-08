namespace VideoInferenceDemo;

public interface IModelReferenceResolver
{
    IReadOnlyList<InspectionModelReference> Resolve(InspectionRecipe recipe);
}
