using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VideoInferenceDemo.ImageInspection.Controls;

public partial class CameraViewportGrid : UserControl
{
    public static readonly DependencyProperty CamerasProperty =
        DependencyProperty.Register(
            nameof(Cameras),
            typeof(IEnumerable),
            typeof(CameraViewportGrid),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ColumnsProperty =
        DependencyProperty.Register(
            nameof(Columns),
            typeof(int),
            typeof(CameraViewportGrid),
            new PropertyMetadata(1));

    public static readonly DependencyProperty SelectCommandProperty =
        DependencyProperty.Register(
            nameof(SelectCommand),
            typeof(ICommand),
            typeof(CameraViewportGrid),
            new PropertyMetadata(null));

    public CameraViewportGrid()
    {
        InitializeComponent();
    }

    public IEnumerable? Cameras
    {
        get => (IEnumerable?)GetValue(CamerasProperty);
        set => SetValue(CamerasProperty, value);
    }

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public ICommand? SelectCommand
    {
        get => (ICommand?)GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }
}
