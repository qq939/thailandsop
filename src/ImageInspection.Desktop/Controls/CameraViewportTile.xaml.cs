using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VideoInferenceDemo.ImageInspection.Controls;

public partial class CameraViewportTile : UserControl
{
    public static readonly DependencyProperty CameraProperty =
        DependencyProperty.Register(
            nameof(Camera),
            typeof(InspectionCameraSessionViewModel),
            typeof(CameraViewportTile),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SelectCommandProperty =
        DependencyProperty.Register(
            nameof(SelectCommand),
            typeof(ICommand),
            typeof(CameraViewportTile),
            new PropertyMetadata(null));

    public CameraViewportTile()
    {
        InitializeComponent();
    }

    public InspectionCameraSessionViewModel? Camera
    {
        get => (InspectionCameraSessionViewModel?)GetValue(CameraProperty);
        set => SetValue(CameraProperty, value);
    }

    public ICommand? SelectCommand
    {
        get => (ICommand?)GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }
}
