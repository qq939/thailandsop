using System.Windows;
using System.ComponentModel;

namespace VideoInferenceDemo.ImageInspection.Settings;

public partial class InspectionParameterSettingsWindow : Window
{
    public InspectionParameterSettingsWindow(InspectionParameterSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InspectionParameterSettingsViewModel.SaveSucceeded) &&
            DataContext is InspectionParameterSettingsViewModel { SaveSucceeded: true })
        {
            DialogResult = true;
        }
    }
}
