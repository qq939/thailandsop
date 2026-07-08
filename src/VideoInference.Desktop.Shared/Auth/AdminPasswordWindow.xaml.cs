using System.Windows;
using System.Windows.Input;

namespace VideoInferenceDemo;

public partial class AdminPasswordWindow : Window
{
    private readonly PersonnelRepository _repository;

    public AdminPasswordWindow(PersonnelRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        InitializeComponent();
        Loaded += (_, _) => PasswordInput.Focus();
    }

    public static bool Confirm(Window owner, PersonnelRepository repository)
    {
        var window = new AdminPasswordWindow(repository)
        {
            Owner = owner
        };

        return window.ShowDialog() == true;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (!_repository.VerifyAdminPassword(PasswordInput.Password))
        {
            StatusTextBlock.Text = "Admin 密码不正确。";
            PasswordInput.SelectAll();
            PasswordInput.Focus();
            return;
        }

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnConfirm(sender, e);
            e.Handled = true;
        }
    }
}
