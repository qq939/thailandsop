using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ImageBox;

namespace VideoInferenceDemo.ImageInspection.Roi;

#pragma warning disable CA1416
public sealed partial class InspectionRoiConfigViewModel : ObservableObject
{
    private double _coordinateImageWidth;
    private double _coordinateImageHeight;
    private bool _syncingText;

    public InspectionRoiConfigViewModel(InspectionRoiConfig config)
    {
        Id = config.Id;
        Name = config.Name;
        Enabled = config.Enabled;
        CameraId = config.CameraId;
        CenterX = config.CenterX;
        CenterY = config.CenterY;
        Width = config.Width;
        Height = config.Height;
        AngleDeg = config.AngleDeg;
        ModelId = config.ModelId;
        SortOrder = config.SortOrder;
        SyncAllNumberText();
    }

    [ObservableProperty] private string id = string.Empty;
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private bool enabled = true;
    [ObservableProperty] private string cameraId = string.Empty;
    [ObservableProperty] private double centerX = 0.5;
    [ObservableProperty] private double centerY = 0.5;
    [ObservableProperty] private double width = 0.25;
    [ObservableProperty] private double height = 0.25;
    [ObservableProperty] private double angleDeg;
    [ObservableProperty] private string modelId = string.Empty;
    [ObservableProperty] private int sortOrder;
    [ObservableProperty] private string centerXText = "0.5";
    [ObservableProperty] private string centerYText = "0.5";
    [ObservableProperty] private string widthText = "0.25";
    [ObservableProperty] private string heightText = "0.25";
    [ObservableProperty] private string angleDegText = "0";

    public string Summary => HasPixelCoordinateSpace
        ? $"Center ({ToPixelX(CenterX):0.#}px, {ToPixelY(CenterY):0.#}px) / Size ({ToPixelX(Width):0.#} x {ToPixelY(Height):0.#}px) / Angle {AngleDeg:0.#}"
        : $"Center ({CenterX:0.###}, {CenterY:0.###}) / Size ({Width:0.###} x {Height:0.###}) / Angle {AngleDeg:0.#}";

    private bool HasPixelCoordinateSpace => _coordinateImageWidth > 0 && _coordinateImageHeight > 0;

    public void SetCoordinateSpace(double imageWidth, double imageHeight)
    {
        _coordinateImageWidth = double.IsFinite(imageWidth) && imageWidth > 0 ? imageWidth : 0;
        _coordinateImageHeight = double.IsFinite(imageHeight) && imageHeight > 0 ? imageHeight : 0;
        SyncAllNumberText();
        NotifyDependentPropertiesChanged();
    }

    partial void OnNameChanged(string value) => NotifyDependentPropertiesChanged();

    partial void OnEnabledChanged(bool value) => NotifyDependentPropertiesChanged();

    partial void OnCameraIdChanged(string value) => NotifyDependentPropertiesChanged();

    partial void OnCenterXChanged(double value)
    {
        SyncNumberText(CenterXText, FormatX(value), static (self, text) => self.CenterXText = text);
        NotifyDependentPropertiesChanged();
    }

    partial void OnCenterYChanged(double value)
    {
        SyncNumberText(CenterYText, FormatY(value), static (self, text) => self.CenterYText = text);
        NotifyDependentPropertiesChanged();
    }

    partial void OnWidthChanged(double value)
    {
        SyncNumberText(WidthText, FormatX(value), static (self, text) => self.WidthText = text);
        NotifyDependentPropertiesChanged();
    }

    partial void OnHeightChanged(double value)
    {
        SyncNumberText(HeightText, FormatY(value), static (self, text) => self.HeightText = text);
        NotifyDependentPropertiesChanged();
    }

    partial void OnAngleDegChanged(double value)
    {
        SyncNumberText(AngleDegText, FormatNumber(value), static (self, text) => self.AngleDegText = text);
        NotifyDependentPropertiesChanged();
    }

    partial void OnModelIdChanged(string value) => NotifyDependentPropertiesChanged();

    partial void OnCenterXTextChanged(string value)
    {
        if (_syncingText)
        {
            return;
        }

        if (TryParseNumber(value, out var parsed))
        {
            CenterX = HasPixelCoordinateSpace ? parsed / _coordinateImageWidth : parsed;
        }
    }

    partial void OnCenterYTextChanged(string value)
    {
        if (_syncingText)
        {
            return;
        }

        if (TryParseNumber(value, out var parsed))
        {
            CenterY = HasPixelCoordinateSpace ? parsed / _coordinateImageHeight : parsed;
        }
    }

    partial void OnWidthTextChanged(string value)
    {
        if (_syncingText)
        {
            return;
        }

        if (TryParseNumber(value, out var parsed))
        {
            Width = HasPixelCoordinateSpace ? parsed / _coordinateImageWidth : parsed;
        }
    }

    partial void OnHeightTextChanged(string value)
    {
        if (_syncingText)
        {
            return;
        }

        if (TryParseNumber(value, out var parsed))
        {
            Height = HasPixelCoordinateSpace ? parsed / _coordinateImageHeight : parsed;
        }
    }

    partial void OnAngleDegTextChanged(string value)
    {
        if (_syncingText)
        {
            return;
        }

        if (TryParseNumber(value, out var parsed))
        {
            AngleDeg = parsed;
        }
    }

    public InspectionRoiConfig Build()
    {
        return new InspectionRoiConfig
        {
            Id = Id,
            Name = Name,
            Enabled = Enabled,
            CameraId = CameraId,
            CenterX = CenterX,
            CenterY = CenterY,
            Width = Width,
            Height = Height,
            AngleDeg = AngleDeg,
            ModelId = ModelId,
            SortOrder = SortOrder
        };
    }

    public ImageOverlayItem ToOverlay(double imageWidth, double imageHeight, bool isSelected)
    {
        var strokeHex = isSelected ? "#F28C28" : "#3DA5F4";
        var alpha = isSelected ? (byte)48 : (byte)36;
        var stroke = (Color)ColorConverter.ConvertFromString(strokeHex);
        var roiWidth = Width * imageWidth;
        var roiHeight = Height * imageHeight;
        var left = (CenterX * imageWidth) - (roiWidth / 2);
        var top = (CenterY * imageHeight) - (roiHeight / 2);

        return new ImageOverlayItem
        {
            Kind = ImageOverlayKind.RotatedRectangle,
            X = left,
            Y = top,
            Width = roiWidth,
            Height = roiHeight,
            Angle = AngleDeg,
            Stroke = new SolidColorBrush(stroke),
            Fill = new SolidColorBrush(Color.FromArgb(alpha, stroke.R, stroke.G, stroke.B)),
            StrokeThickness = isSelected ? 3 : 2
        };
    }

    public ImageOverlayItem ToTextOverlay(double imageWidth, double imageHeight, bool isSelected)
    {
        var strokeHex = isSelected ? "#F28C28" : "#3DA5F4";
        var stroke = (Color)ColorConverter.ConvertFromString(strokeHex);
        var roiWidth = Width * imageWidth;
        var roiHeight = Height * imageHeight;
        var left = (CenterX * imageWidth) - (roiWidth / 2);
        var top = (CenterY * imageHeight) - (roiHeight / 2);

        return new ImageOverlayItem
        {
            Kind = ImageOverlayKind.Text,
            Text = Name,
            X = left,
            Y = Math.Max(0, top - 22),
            FontSize = isSelected ? 18 : 16,
            Foreground = new SolidColorBrush(stroke)
        };
    }

    private void NotifyDependentPropertiesChanged()
    {
        OnPropertyChanged(nameof(Summary));
    }

    private void SyncAllNumberText()
    {
        _syncingText = true;
        try
        {
            SyncNumberText(CenterXText, FormatX(CenterX), static (self, text) => self.CenterXText = text);
            SyncNumberText(CenterYText, FormatY(CenterY), static (self, text) => self.CenterYText = text);
            SyncNumberText(WidthText, FormatX(Width), static (self, text) => self.WidthText = text);
            SyncNumberText(HeightText, FormatY(Height), static (self, text) => self.HeightText = text);
            SyncNumberText(AngleDegText, FormatNumber(AngleDeg), static (self, text) => self.AngleDegText = text);
        }
        finally
        {
            _syncingText = false;
        }
    }

    private string FormatX(double normalized) => FormatNumber(HasPixelCoordinateSpace ? ToPixelX(normalized) : normalized);

    private string FormatY(double normalized) => FormatNumber(HasPixelCoordinateSpace ? ToPixelY(normalized) : normalized);

    private double ToPixelX(double normalized) => normalized * _coordinateImageWidth;

    private double ToPixelY(double normalized) => normalized * _coordinateImageHeight;

    private static bool TryParseNumber(string value, out double result)
    {
        value = value?.Trim().Replace(',', '.') ?? string.Empty;
        return double.TryParse(
            value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out result);
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void SyncNumberText(string current, string formatted, Action<InspectionRoiConfigViewModel, string> assign)
    {
        if (string.Equals(current, formatted, StringComparison.Ordinal))
        {
            return;
        }

        assign(this, formatted);
    }
}
