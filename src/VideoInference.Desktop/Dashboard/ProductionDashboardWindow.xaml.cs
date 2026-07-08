using System.Windows;

namespace VideoInferenceDemo;

public partial class ProductionDashboardWindow : Window
{
    public ProductionDashboardWindow(ProductionDashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
