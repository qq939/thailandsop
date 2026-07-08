using System.Collections.Generic;
using System.Linq;

namespace VideoInferenceDemo.ImageInspection;

public sealed class InspectionRecipeCatalog
{
    public List<InspectionRecipeEntry> Recipes { get; set; } = [];

    public InspectionRecipeEntry GetOrCreate(string productModel, string taskId, string positionNo)
    {
        taskId = string.IsNullOrWhiteSpace(taskId) ? "task-main" : taskId.Trim();
        var recipe = Recipes.FirstOrDefault(item =>
            string.Equals(item.ProductModel, productModel, System.StringComparison.OrdinalIgnoreCase) &&
            string.Equals(string.IsNullOrWhiteSpace(item.TaskId) ? "task-main" : item.TaskId, taskId, System.StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.PositionNo, positionNo, System.StringComparison.OrdinalIgnoreCase));

        if (recipe != null)
        {
            return recipe;
        }

        recipe = new InspectionRecipeEntry
        {
            ProductModel = productModel,
            TaskId = taskId,
            TaskName = taskId,
            PositionNo = positionNo,
            Rois =
            [
                new InspectionRoiConfig
                {
                    Id = "roi-a",
                    Name = "ROI-A",
                    CenterX = 0.321,
                    CenterY = 0.284,
                    Width = 0.282,
                    Height = 0.146,
                    AngleDeg = -8,
                    ModelId = "seal-check-v2",
                    SortOrder = 1
                },
                new InspectionRoiConfig
                {
                    Id = "roi-b",
                    Name = "ROI-B",
                    CenterX = 0.612,
                    CenterY = 0.516,
                    Width = 0.236,
                    Height = 0.124,
                    AngleDeg = 12,
                    ModelId = "surface-ng-v1",
                    SortOrder = 2
                }
            ],
            ModelBindings =
            [
                new RecipeModelBindingEntry
                {
                    ModelId = "seal-check-v2",
                    Alias = "seal-check-v2",
                    Sequence = 1,
                    RoiIds = ["roi-a"]
                },
                new RecipeModelBindingEntry
                {
                    ModelId = "surface-ng-v1",
                    Alias = "surface-ng-v1",
                    Sequence = 2,
                    RoiIds = ["roi-b"]
                }
            ]
        };

        Recipes.Add(recipe);
        return recipe;
    }

    public static InspectionRecipeCatalog CreateDefault()
    {
        var catalog = new InspectionRecipeCatalog();
        catalog.GetOrCreate("A100", "task-main", "P01");
        return catalog;
    }
}
