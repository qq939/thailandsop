using System.Windows;
using System.ComponentModel;

namespace VideoInferenceDemo.ImageInspection.Camera;

public partial class InspectionCameraConfigWindow : Window
{
    public InspectionCameraConfigWindow(InspectionCameraConfigViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InspectionCameraConfigViewModel.SaveSucceeded) &&
            DataContext is InspectionCameraConfigViewModel { SaveSucceeded: true })
        {
            DialogResult = true;
        }
    }
}
