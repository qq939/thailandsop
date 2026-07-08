using System.Windows.Media;

namespace VideoInferenceDemo.ImageInspection;

public sealed class RoiListItemViewModel
{
    public string Name { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string ModelSummary { get; set; } = string.Empty;

    public string StatusText { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    public string CenterX { get; set; } = string.Empty;

    public string CenterY { get; set; } = string.Empty;

    public string Width { get; set; } = string.Empty;

    public string Height { get; set; } = string.Empty;

    public string AngleDeg { get; set; } = string.Empty;

    public double LeftPx { get; set; }

    public double TopPx { get; set; }

    public double WidthPx { get; set; }

    public double HeightPx { get; set; }

    public Brush StrokeBrush { get; set; } = Brushes.Orange;

    public Brush FillBrush { get; set; } = new SolidColorBrush(Color.FromArgb(40, 242, 140, 40));
}
