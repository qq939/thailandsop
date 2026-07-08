using System;

namespace VideoInferenceDemo.ImageInspection;

public sealed class InspectionCameraAlignmentConfig
{
    public bool Enabled { get; set; }

    public string LocatorModelId { get; set; } = string.Empty;

    public int LocatorClassId { get; set; } = -1;

    public string LocatorClassName { get; set; } = string.Empty;

    public double CenterX { get; set; } = 0.5;

    public double CenterY { get; set; } = 0.5;

    public double Width { get; set; } = 0.25;

    public double Height { get; set; } = 0.25;

    public double AngleDeg { get; set; }

    public InspectionCameraAlignmentConfig Normalize()
    {
        return new InspectionCameraAlignmentConfig
        {
            Enabled = Enabled,
            LocatorModelId = LocatorModelId?.Trim() ?? string.Empty,
            LocatorClassId = LocatorClassId,
            LocatorClassName = LocatorClassName?.Trim() ?? string.Empty,
            CenterX = Clamp01(CenterX, 0.5),
            CenterY = Clamp01(CenterY, 0.5),
            Width = Clamp01(Width, 0.25),
            Height = Clamp01(Height, 0.25),
            AngleDeg = double.IsFinite(AngleDeg) ? AngleDeg : 0
        };
    }

    private static double Clamp01(double value, double fallback)
    {
        if (!double.IsFinite(value))
        {
            return fallback;
        }

        return Math.Max(0.001, Math.Min(1.0, value));
    }
}
