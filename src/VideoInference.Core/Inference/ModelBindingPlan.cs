namespace VideoInferenceDemo;

public sealed class ModelBindingPlan
{
    public ModelBindingPlan(
        ModelCatalogEntry model,
        string[]? classNames,
        string? boxColor,
        string[]? boxColors,
        int? boxThickness,
        double? labelFontScale)
    {
        Model = model;
        ClassNames = classNames;
        BoxColor = boxColor;
        BoxColors = boxColors;
        BoxThickness = boxThickness;
        LabelFontScale = labelFontScale;
    }

    public ModelCatalogEntry Model { get; }
    public string[]? ClassNames { get; }
    public string? BoxColor { get; }
    public string[]? BoxColors { get; }
    public int? BoxThickness { get; }
    public double? LabelFontScale { get; }
}
