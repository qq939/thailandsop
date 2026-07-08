using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoInferenceDemo;

public sealed partial class LoginDialogViewModel : ObservableObject
{
    private readonly PersonnelAuthenticationService _authenticationService;

    public LoginDialogViewModel(PersonnelAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        ReloadOptions();
    }

    public ObservableCollection<PersonnelOptionItem> PersonnelOptions { get; } = [];

    [ObservableProperty]
    private PersonnelOptionItem? selectedPersonnel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string statusText = string.Empty;

    public bool HasError => !string.IsNullOrEmpty(StatusText);

    public PersonnelSession? Session { get; private set; }

    public void ReloadOptions()
    {
        PersonnelOptions.Clear();
        foreach (var item in _authenticationService.ListLoginOptions())
        {
            PersonnelOptions.Add(item);
        }

        SelectedPersonnel = PersonnelOptions.FirstOrDefault(item => PersonnelRepository.IsAdminCode(item.EmployeeCode))
                            ?? PersonnelOptions.FirstOrDefault();
    }

    public bool TryLogin(string? passwordText)
    {
        if (SelectedPersonnel == null)
        {
            StatusText = "请选择登录用户。";
            return false;
        }

        var result = _authenticationService.Login(SelectedPersonnel.EmployeeCode, passwordText);
        if (!result.Success)
        {
            StatusText = result.ErrorMessage;
            return false;
        }

        Session = result.Session;
        StatusText = string.Empty;
        return true;
    }
}
