using System.Windows;

namespace VideoInferenceDemo;

public partial class ActionLabelWindow : Window
{
    public ActionLabelWindow(ActionLabelViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
