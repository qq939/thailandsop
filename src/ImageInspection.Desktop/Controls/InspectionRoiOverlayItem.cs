using System.Windows.Media;

namespace VideoInferenceDemo.ImageInspection.Controls;

public sealed class InspectionRoiOverlayItem
{
    public required string Name { get; init; }

    public double LeftPx { get; init; }

    public double TopPx { get; init; }

    public double WidthPx { get; init; }

    public double HeightPx { get; init; }

    public double AngleDeg { get; init; }

    public Brush StrokeBrush { get; init; } = Brushes.Orange;

    public Brush FillBrush { get; init; } = new SolidColorBrush(Color.FromArgb(40, 242, 140, 40));
}
