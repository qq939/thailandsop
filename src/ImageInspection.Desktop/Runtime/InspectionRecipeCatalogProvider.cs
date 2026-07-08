using System.Linq;

namespace VideoInferenceDemo.ImageInspection.Runtime;

public sealed class InspectionRecipeCatalogProvider : IInspectionRecipeProvider
{
    private readonly string _catalogPath;

    public InspectionRecipeCatalogProvider(string catalogPath)
    {
        _catalogPath = catalogPath;
    }

    public InspectionRecipe Get(InspectionRecipeKey key)
    {
        var catalog = InspectionRecipeCatalogStorage.Load(_catalogPath);
        var recipe = catalog.GetOrCreate(key.ProductModel, key.TaskId, key.PositionNo);

        return new InspectionRecipe
        {
            Key = new InspectionRecipeKey(recipe.ProductModel, recipe.TaskId, recipe.PositionNo),
            Calibration = CalibrationContext.Empty,
            Rois = recipe.Rois
                .Select(roi => new RoiDefinition
                {
                    Id = roi.Id,
                    Name = roi.Name,
                    CameraId = string.IsNullOrWhiteSpace(roi.CameraId) ? null : roi.CameraId.Trim(),
                    CenterX = roi.CenterX,
                    CenterY = roi.CenterY,
                    Width = roi.Width,
                    Height = roi.Height,
                    AngleDeg = roi.AngleDeg,
                    Enabled = roi.Enabled,
                    ModelId = string.IsNullOrWhiteSpace(roi.ModelId) ? null : roi.ModelId.Trim(),
                    SortOrder = roi.SortOrder
                })
                .ToArray(),
            ModelBindings = recipe.ModelBindings
                .Select(binding => new InspectionModelReference
                {
                    ModelId = binding.ModelId,
                    Alias = string.IsNullOrWhiteSpace(binding.Alias) ? null : binding.Alias.Trim(),
                    Sequence = binding.Sequence,
                    RoiIds = binding.RoiIds
                })
                .ToArray(),
            AlignmentByCameraId = recipe.AlignmentByCameraId
                .Where(item => !string.IsNullOrWhiteSpace(item.Key) && item.Value != null)
                .ToDictionary(
                    item => item.Key.Trim(),
                    item =>
                    {
                        var alignment = item.Value.Normalize();
                        return new CameraAlignmentDefinition
                        {
                            Enabled = alignment.Enabled,
                            LocatorModelId = alignment.LocatorModelId,
                            LocatorClassId = alignment.LocatorClassId,
                            LocatorClassName = alignment.LocatorClassName,
                            CenterX = alignment.CenterX,
                            CenterY = alignment.CenterY,
                            Width = alignment.Width,
                            Height = alignment.Height,
                            AngleDeg = alignment.AngleDeg
                        };
                    },
                    StringComparer.OrdinalIgnoreCase),
            Parameters = recipe.Parameters
        };
    }
}
