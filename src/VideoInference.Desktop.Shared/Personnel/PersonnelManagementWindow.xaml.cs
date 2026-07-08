using System.Windows;

namespace VideoInferenceDemo;

public partial class PersonnelManagementWindow : Window
{
    public PersonnelManagementWindow(PersonnelManagementViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnClosed(e);
    }
}
