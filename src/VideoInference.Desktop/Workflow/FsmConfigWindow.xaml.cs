using System.Windows;

namespace VideoInferenceDemo;

public partial class FsmConfigWindow : Window
{
    public FsmConfigWindow(FsmConfigViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FsmConfigViewModel vm)
        {
            return;
        }

        if (vm.TrySave())
        {
            DialogResult = true;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
