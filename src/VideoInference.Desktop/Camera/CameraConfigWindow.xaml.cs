using System.Windows;

namespace VideoInferenceDemo;

public partial class CameraConfigWindow : Window
{
    public CameraConfigWindow(CameraConfigViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CameraConfigViewModel vm)
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
