using System.Windows;
using System.Windows.Input;

namespace VideoInferenceDemo;

public partial class PasswordChangeWindow : Window
{
    public PasswordChangeWindow(string employeeCode, string employeeName)
    {
        InitializeComponent();
        TargetUserTextBlock.Text = string.IsNullOrWhiteSpace(employeeName)
            ? $"员工：{employeeCode}"
            : $"员工：{employeeName}（{employeeCode}）";
        Loaded += (_, _) => NewPasswordInput.Focus();
    }

    public string PasswordText { get; private set; } = string.Empty;

    public static string? Request(Window owner, PersonnelEditorItem item)
    {
        var window = new PasswordChangeWindow(item.EmployeeCode, item.EmployeeName)
        {
            Owner = owner
        };

        return window.ShowDialog() == true ? window.PasswordText : null;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(NewPasswordInput.Password, ConfirmPasswordInput.Password, StringComparison.Ordinal))
        {
            StatusTextBlock.Text = "两次输入的密码不一致。";
            ConfirmPasswordInput.SelectAll();
            ConfirmPasswordInput.Focus();
            return;
        }

        PasswordText = NewPasswordInput.Password;
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
