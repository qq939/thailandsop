namespace VideoInferenceDemo;

public sealed record PersonnelSession(
    string EmployeeCode,
    string EmployeeName,
    string Team,
    bool IsAdmin)
{
    public string DisplayText => string.IsNullOrWhiteSpace(EmployeeName) || string.Equals(EmployeeName, EmployeeCode, StringComparison.OrdinalIgnoreCase)
        ? EmployeeCode
        : $"{EmployeeName} ({EmployeeCode})";

    public PersonnelOptionItem ToOptionItem() => new(EmployeeCode, EmployeeName, Team);

    public static PersonnelSession FromRecord(PersonnelRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new PersonnelSession(
            record.EmployeeCode,
            record.EmployeeName,
            record.Team,
            record.IsAdmin);
    }
}

public sealed record PersonnelLoginResult(
    bool Success,
    PersonnelSession? Session,
    string ErrorMessage)
{
    public static PersonnelLoginResult Failed(string errorMessage) => new(false, null, errorMessage);
}

public sealed class PersonnelAuthenticationService
{
    private readonly PersonnelRepository _repository;

    public PersonnelAuthenticationService(PersonnelRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _repository.EnsureDefaultAdmin();
    }

    public event EventHandler? CurrentSessionChanged;

    public PersonnelSession? CurrentSession { get; private set; }

    public bool IsLoggedIn => CurrentSession != null;

    public IReadOnlyList<PersonnelOptionItem> ListLoginOptions()
    {
        _repository.EnsureDefaultAdmin();
        return _repository.List(includeInactive: false)
            .Select(record => new PersonnelOptionItem(record.EmployeeCode, record.EmployeeName, record.Team))
            .ToList();
    }

    public PersonnelLoginResult Login(string employeeCode, string? passwordText)
    {
        if (string.IsNullOrWhiteSpace(employeeCode))
        {
            return PersonnelLoginResult.Failed("请选择登录用户。");
        }

        var record = _repository.GetByCode(employeeCode.Trim());
        if (record == null || !record.IsActive)
        {
            return PersonnelLoginResult.Failed("用户不存在或已停用。");
        }

        if (!_repository.VerifyPassword(record.EmployeeCode, passwordText, requireActive: true))
        {
            return PersonnelLoginResult.Failed("密码不正确。");
        }

        CurrentSession = PersonnelSession.FromRecord(record);
        CurrentSessionChanged?.Invoke(this, EventArgs.Empty);
        return new PersonnelLoginResult(true, CurrentSession, string.Empty);
    }

    public void Logout()
    {
        if (CurrentSession == null)
        {
            return;
        }

        CurrentSession = null;
        CurrentSessionChanged?.Invoke(this, EventArgs.Empty);
    }
}
