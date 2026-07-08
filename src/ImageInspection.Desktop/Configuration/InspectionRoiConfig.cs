using System;

namespace VideoInferenceDemo.ImageInspection;

public sealed class InspectionRoiConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "ROI";

    public bool Enabled { get; set; } = true;

    public string CameraId { get; set; } = string.Empty;

    public double CenterX { get; set; } = 0.5;

    public double CenterY { get; set; } = 0.5;

    public double Width { get; set; } = 0.25;

    public double Height { get; set; } = 0.25;

    public double AngleDeg { get; set; }

    public string ModelId { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}
