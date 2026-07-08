using System.ComponentModel;
using System.Windows;

namespace VideoInferenceDemo.ImageInspection.Tasks;

public partial class InspectionTaskConfigWindow : Window
{
    private readonly InspectionTaskConfigViewModel _viewModel;

    public InspectionTaskConfigWindow(InspectionTaskConfigViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnClosed;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InspectionTaskConfigViewModel.SaveSucceeded) &&
            DataContext is InspectionTaskConfigViewModel { SaveSucceeded: true })
        {
            DialogResult = true;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.CancelProbe();
    }
}
