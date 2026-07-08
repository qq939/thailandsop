using System.Windows;
using System.ComponentModel;

namespace VideoInferenceDemo.ImageInspection.Roi;

public partial class InspectionRoiConfigWindow : Window
{
    public InspectionRoiConfigWindow(InspectionRoiConfigWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InspectionRoiConfigWindowViewModel.SaveSucceeded) &&
            DataContext is InspectionRoiConfigWindowViewModel { SaveSucceeded: true })
        {
            DialogResult = true;
        }
    }

}
