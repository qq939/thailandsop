using System.Windows;

namespace VideoInferenceDemo;

public partial class PerformanceDiagnosticsWindow : Window
{
    public PerformanceDiagnosticsWindow(PerformanceDiagnosticsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
