namespace VideoInferenceDemo;

public interface IInspectionRecipeProvider
{
    InspectionRecipe Get(InspectionRecipeKey key);
}
