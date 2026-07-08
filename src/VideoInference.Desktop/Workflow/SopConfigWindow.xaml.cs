using System.Windows;

namespace VideoInferenceDemo;

public partial class SopConfigWindow : Window
{
    public SopConfigWindow(SopConfigViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SopConfigViewModel vm)
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
