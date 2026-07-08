using System.Windows;

namespace VideoInferenceDemo;

public partial class SystemSettingsWindow : Window
{
    public SystemSettingsWindow(SystemSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SystemSettingsViewModel vm)
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
