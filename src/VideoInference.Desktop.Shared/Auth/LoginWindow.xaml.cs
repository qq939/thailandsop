using System.Windows;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;

namespace VideoInferenceDemo;

public partial class LoginWindow : Window
{
    private readonly LoginDialogViewModel _viewModel;

    public LoginWindow(PersonnelAuthenticationService authenticationService)
    {
        InitializeComponent();
        _viewModel = new LoginDialogViewModel(authenticationService);
        DataContext = _viewModel;
        Loaded += (_, _) =>
        {
            PasswordInput.Focus();
            PasswordInput.SelectAll();
        };
    }

    public PersonnelSession? Session => _viewModel.Session;

    private void OnLogin(object sender, RoutedEventArgs e)
    {
        // Ensure password is synced back from visible mode
        if (PasswordVisibleInput.Visibility == Visibility.Visible)
        {
            PasswordInput.Password = PasswordVisibleInput.Text;
        }

        if (!_viewModel.TryLogin(PasswordInput.Password))
        {
            PasswordInput.Focus();
            PasswordInput.SelectAll();
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
            OnLogin(sender, e);
            e.Handled = true;
        }
    }

    private void OnTogglePasswordVisibility(object sender, RoutedEventArgs e)
    {
        if (PwToggleButton.IsChecked == true)
        {
            // Switch to visible password mode
            PasswordVisibleInput.Text = PasswordInput.Password;
            PasswordVisibleInput.Visibility = Visibility.Visible;
            PasswordInput.Visibility = Visibility.Collapsed;
            PwToggleIcon.Kind = PackIconKind.Eye;
            PwToggleButton.ToolTip = "隐藏密码";
            PasswordVisibleInput.Focus();
            PasswordVisibleInput.SelectAll();
        }
        else
        {
            // Switch to hidden password mode
            PasswordInput.Password = PasswordVisibleInput.Text;
            PasswordVisibleInput.Visibility = Visibility.Collapsed;
            PasswordInput.Visibility = Visibility.Visible;
            PwToggleIcon.Kind = PackIconKind.EyeOff;
            PwToggleButton.ToolTip = "显示密码";
            PasswordInput.Focus();
            PasswordInput.SelectAll();
        }
    }
}
