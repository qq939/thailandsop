using System;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ImageBox;

namespace VideoInferenceDemo.ImageInspection.Roi;

#pragma warning disable CA1416
public sealed partial class InspectionCameraAlignmentConfigViewModel : ObservableObject
{
    private double _coordinateImageWidth;
    private double _coordinateImageHeight;
    private bool _syncingText;

    public InspectionCameraAlignmentConfigViewModel(InspectionCameraAlignmentConfig? config)
    {
        var normalized = (config ?? new InspectionCameraAlignmentConfig()).Normalize();
        Enabled = normalized.Enabled;
        LocatorModelId = normalized.LocatorModelId;
        LocatorClassId = normalized.LocatorClassId;
        LocatorClassName = normalized.LocatorClassName;
        CenterX = normalized.CenterX;
        CenterY = normalized.CenterY;
        Width = normalized.Width;
        Height = normalized.Height;
        AngleDeg = normalized.AngleDeg;
        SyncAllNumberText();
    }

    [ObservableProperty] private bool enabled;
    [ObservableProperty] private string locatorModelId = string.Empty;
    [ObservableProperty] private int locatorClassId = -1;
    [ObservableProperty] private string locatorClassName = string.Empty;
    [ObservableProperty] private double centerX = 0.5;
    [ObservableProperty] private double centerY = 0.5;
    [ObservableProperty] private double width = 0.25;
    [ObservableProperty] private double height = 0.25;
    [ObservableProperty] private double angleDeg;
    [ObservableProperty] private string centerXText = "0.5";
    [ObservableProperty] private string centerYText = "0.5";
    [ObservableProperty] private string widthText = "0.25";
    [ObservableProperty] private string heightText = "0.25";
    [ObservableProperty] private string angleDegText = "0";

    public string Summary => Enabled
        ? HasPixelCoordinateSpace
            ? $"定位 {LocatorModelId} / {LocatorClassDisplay} / Center ({ToPixelX(CenterX):0.#}px, {ToPixelY(CenterY):0.#}px) / Angle {AngleDeg:0.#}"
            : $"定位 {LocatorModelId} / {LocatorClassDisplay} / Center ({CenterX:0.###}, {CenterY:0.###}) / Angle {AngleDeg:0.#}"
        : "未启用";

    public string LocatorClassDisplay => !string.IsNullOrWhiteSpace(LocatorClassName)
        ? LocatorClassName
        : LocatorClassId >= 0
            ? $"#{LocatorClassId}"
            : "未选择";

    private bool HasPixelCoordinateSpace => _coordinateImageWidth > 0 && _coordinateImageHeight > 0;

    public void SetCoordinateSpace(double imageWidth, double imageHeight)
    {
        _coordinateImageWidth = double.IsFinite(imageWidth) && imageWidth > 0 ? imageWidth : 0;
        _coordinateImageHeight = double.IsFinite(imageHeight) && imageHeight > 0 ? imageHeight : 0;
        SyncAllNumberText();
        NotifyDependentPropertiesChanged();
    }

    partial void OnEnabledChanged(bool value) => NotifyDependentPropertiesChanged();
    partial void OnLocatorModelIdChanged(string value) => NotifyDependentPropertiesChanged();
    partial void OnLocatorClassIdChanged(int value) => NotifyDependentPropertiesChanged();
    partial void OnLocatorClassNameChanged(string value) => NotifyDependentPropertiesChanged();

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

    public InspectionCameraAlignmentConfig Build()
    {
        return new InspectionCameraAlignmentConfig
        {
            Enabled = Enabled,
            LocatorModelId = LocatorModelId,
            LocatorClassId = LocatorClassId,
            LocatorClassName = LocatorClassName,
            CenterX = CenterX,
            CenterY = CenterY,
            Width = Width,
            Height = Height,
            AngleDeg = AngleDeg
        }.Normalize();
    }

    public ImageOverlayItem ToOverlay(double imageWidth, double imageHeight)
    {
        var stroke = (Color)ColorConverter.ConvertFromString("#10B981");
        var boxWidth = Width * imageWidth;
        var boxHeight = Height * imageHeight;
        var left = (CenterX * imageWidth) - (boxWidth / 2);
        var top = (CenterY * imageHeight) - (boxHeight / 2);

        return new ImageOverlayItem
        {
            Kind = ImageOverlayKind.RotatedRectangle,
            X = left,
            Y = top,
            Width = boxWidth,
            Height = boxHeight,
            Angle = AngleDeg,
            Stroke = new SolidColorBrush(stroke),
            Fill = new SolidColorBrush(Color.FromArgb(34, stroke.R, stroke.G, stroke.B)),
            StrokeThickness = 3
        };
    }

    public ImageOverlayItem ToTextOverlay(double imageWidth, double imageHeight)
    {
        var stroke = (Color)ColorConverter.ConvertFromString("#10B981");
        var boxWidth = Width * imageWidth;
        var boxHeight = Height * imageHeight;
        var left = (CenterX * imageWidth) - (boxWidth / 2);
        var top = (CenterY * imageHeight) - (boxHeight / 2);

        return new ImageOverlayItem
        {
            Kind = ImageOverlayKind.Text,
            Text = "定位",
            X = left,
            Y = Math.Max(0, top - 26),
            FontSize = 18,
            Foreground = new SolidColorBrush(stroke)
        };
    }

    private void NotifyDependentPropertiesChanged()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(LocatorClassDisplay));
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

    private void SyncNumberText(string current, string formatted, Action<InspectionCameraAlignmentConfigViewModel, string> assign)
    {
        if (string.Equals(current, formatted, StringComparison.Ordinal))
        {
            return;
        }

        assign(this, formatted);
    }
}
