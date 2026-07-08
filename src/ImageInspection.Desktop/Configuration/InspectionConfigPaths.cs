using System;
using System.IO;

namespace VideoInferenceDemo.ImageInspection;

public static class InspectionConfigPaths
{
    public static string CameraSettingsPath => Path.Combine(AppContext.BaseDirectory, "inspection_camera_config.json");

    public static string ParameterSettingsPath => Path.Combine(AppContext.BaseDirectory, "inspection_parameter_config.json");

    public static string TaskSettingsPath => Path.Combine(AppContext.BaseDirectory, "inspection_task_config.json");

    public static string ModelSettingsPath => Path.Combine(AppContext.BaseDirectory, "inspection_model_config.json");

    public static string RecipeCatalogPath => Path.Combine(AppContext.BaseDirectory, "inspection_recipe_config.json");
}
