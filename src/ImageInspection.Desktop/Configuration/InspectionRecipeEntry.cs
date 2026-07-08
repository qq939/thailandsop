using System.Collections.Generic;

namespace VideoInferenceDemo.ImageInspection;

public sealed class InspectionRecipeEntry
{
    public string ProductModel { get; set; } = string.Empty;

    public string TaskId { get; set; } = string.Empty;

    public string TaskName { get; set; } = string.Empty;

    public string PositionNo { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string ReferenceImagePath { get; set; } = string.Empty;

    public Dictionary<string, string> ReferenceImagePathsByCameraId { get; set; } = [];

    public Dictionary<string, InspectionCameraAlignmentConfig> AlignmentByCameraId { get; set; } = [];

    public List<InspectionRoiConfig> Rois { get; set; } = [];

    public List<RecipeModelBindingEntry> ModelBindings { get; set; } = [];

    public Dictionary<string, string> Parameters { get; set; } = [];
}
