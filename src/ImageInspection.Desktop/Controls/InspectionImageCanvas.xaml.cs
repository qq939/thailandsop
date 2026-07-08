using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VideoInferenceDemo.ImageInspection.Controls;

public partial class InspectionImageCanvas : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(InspectionImageCanvas),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ImageSourceProperty =
        DependencyProperty.Register(
            nameof(ImageSource),
            typeof(ImageSource),
            typeof(InspectionImageCanvas),
            new PropertyMetadata(null));

    public static readonly DependencyProperty OverlayItemsProperty =
        DependencyProperty.Register(
            nameof(OverlayItems),
            typeof(IEnumerable),
            typeof(InspectionImageCanvas),
            new PropertyMetadata(null));

    public InspectionImageCanvas()
    {
        InitializeComponent();
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public IEnumerable? OverlayItems
    {
        get => (IEnumerable?)GetValue(OverlayItemsProperty);
        set => SetValue(OverlayItemsProperty, value);
    }
}
