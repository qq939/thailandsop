namespace VideoInferenceDemo.Tests.Storage;

[Collection("DbSession")]
public sealed class PersonnelAuthenticationTests : IDisposable
{
    private readonly string _root;

    public PersonnelAuthenticationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "VideoInferenceDemo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        DbSession.Reset();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public void WorkspaceBootstrap_CreatesDefaultAdmin()
    {
        var paths = WorkspaceDatabaseBootstrap.Initialize(_root);

        var repository = new PersonnelRepository(paths.ConfigDbPath);
        var admin = repository.GetByCode(PersonnelRepository.AdminEmployeeCode);

        Assert.NotNull(admin);
        Assert.Equal(PersonnelRepository.AdminDefaultPassword, admin!.PasswordText);
        Assert.True(admin.IsActive);
    }

    [Fact]
    public void Authentication_AllowsEmptyPassword_AndRejectsWrongPassword()
    {
        var dbPath = Path.Combine(_root, "workspace_config.db");
        DbSession.InitializeSplit(dbPath, Path.Combine(_root, "results"));
        var repository = new PersonnelRepository(dbPath);
        repository.EnsureDefaultAdmin();
        repository.SetPassword(PersonnelRepository.AdminEmployeeCode, string.Empty);
        repository.Upsert("E001", "Operator A", passwordText: string.Empty);
        var auth = new PersonnelAuthenticationService(repository);

        var adminLogin = auth.Login(PersonnelRepository.AdminEmployeeCode, string.Empty);
        var operatorLogin = auth.Login("E001", string.Empty);
        var wrongLogin = auth.Login("E001", "bad");

        Assert.True(adminLogin.Success);
        Assert.True(operatorLogin.Success);
        Assert.False(wrongLogin.Success);
    }

    [Fact]
    public void Repository_DoesNotDeactivateAdmin()
    {
        var dbPath = Path.Combine(_root, "workspace_config.db");
        DbSession.InitializeSplit(dbPath, Path.Combine(_root, "results"));
        var repository = new PersonnelRepository(dbPath);
        repository.EnsureDefaultAdmin();

        var changed = repository.Deactivate(PersonnelRepository.AdminEmployeeCode);

        Assert.False(changed);
        Assert.True(repository.GetByCode(PersonnelRepository.AdminEmployeeCode)!.IsActive);
    }

    [Fact]
    public void PersonnelManagementViewModel_BlocksNonAdminFromAddingPersonnel()
    {
        var dbPath = Path.Combine(_root, "workspace_config.db");
        DbSession.InitializeSplit(dbPath, Path.Combine(_root, "results"));
        var repository = new PersonnelRepository(dbPath);
        repository.EnsureDefaultAdmin();
        repository.Upsert("E001", "Operator A", passwordText: string.Empty);
        var auth = new PersonnelAuthenticationService(repository);
        Assert.True(auth.Login("E001", string.Empty).Success);
        var vm = new PersonnelManagementViewModel(repository, auth, () => true);
        var beforeCount = vm.Personnel.Count;

        vm.AddPersonnelCommand.Execute(null);

        Assert.Equal(beforeCount, vm.Personnel.Count);
        Assert.Contains("Admin", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PersonnelEditorItem_PasswordDisplayText_HidesPlainText()
    {
        var item = new PersonnelEditorItem
        {
            PasswordText = "Secret123"
        };

        Assert.Equal("已设置", item.PasswordDisplayText);
        Assert.DoesNotContain("Secret123", item.PasswordDisplayText, StringComparison.Ordinal);

        item.PasswordText = string.Empty;

        Assert.Equal("空密码", item.PasswordDisplayText);
    }

    [Fact]
    public void PersonnelManagementViewModel_ChangePassword_UsesPasswordRequest()
    {
        var dbPath = Path.Combine(_root, "workspace_config.db");
        DbSession.InitializeSplit(dbPath, Path.Combine(_root, "results"));
        var repository = new PersonnelRepository(dbPath);
        repository.EnsureDefaultAdmin();
        repository.Upsert("E001", "Operator A", passwordText: "old");
        var auth = new PersonnelAuthenticationService(repository);
        Assert.True(auth.Login(PersonnelRepository.AdminEmployeeCode, PersonnelRepository.AdminDefaultPassword).Success);
        var vm = new PersonnelManagementViewModel(
            repository,
            auth,
            () => true,
            item => item.EmployeeCode == "E001" ? string.Empty : "unexpected");
        vm.SelectedPersonnel = vm.Personnel.Single(item => item.EmployeeCode == "E001");

        vm.ChangePasswordSelectedCommand.Execute(null);

        Assert.True(repository.VerifyPassword("E001", string.Empty));
        Assert.Equal(string.Empty, repository.GetByCode("E001")!.PasswordText);
    }

    [Fact]
    public void PersonnelManagementViewModel_CheckboxDoesNotDeactivateAdmin()
    {
        var dbPath = Path.Combine(_root, "workspace_config.db");
        DbSession.InitializeSplit(dbPath, Path.Combine(_root, "results"));
        var repository = new PersonnelRepository(dbPath);
        repository.EnsureDefaultAdmin();
        var auth = new PersonnelAuthenticationService(repository);
        Assert.True(auth.Login(PersonnelRepository.AdminEmployeeCode, PersonnelRepository.AdminDefaultPassword).Success);
        var vm = new PersonnelManagementViewModel(repository, auth, () => true);
        var admin = vm.Personnel.Single(item => PersonnelRepository.IsAdminCode(item.EmployeeCode));

        admin.IsActive = false;

        Assert.True(admin.IsActive);
        Assert.True(repository.GetByCode(PersonnelRepository.AdminEmployeeCode)!.IsActive);
        Assert.Contains("Admin 不能停用", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PersonnelManagementViewModel_CheckboxRequiresAdminAccess()
    {
        var dbPath = Path.Combine(_root, "workspace_config.db");
        DbSession.InitializeSplit(dbPath, Path.Combine(_root, "results"));
        var repository = new PersonnelRepository(dbPath);
        repository.EnsureDefaultAdmin();
        repository.Upsert("E001", "Operator A", isActive: true, passwordText: string.Empty);
        var auth = new PersonnelAuthenticationService(repository);
        Assert.True(auth.Login("E001", string.Empty).Success);
        var vm = new PersonnelManagementViewModel(repository, auth, () => true);
        var item = vm.Personnel.Single(person => person.EmployeeCode == "E001");

        item.IsActive = false;

        Assert.True(item.IsActive);
        Assert.True(repository.GetByCode("E001")!.IsActive);
        Assert.Contains("Admin", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PersonnelManagementViewModel_CheckboxPersistsActiveChange()
    {
        var dbPath = Path.Combine(_root, "workspace_config.db");
        DbSession.InitializeSplit(dbPath, Path.Combine(_root, "results"));
        var repository = new PersonnelRepository(dbPath);
        repository.EnsureDefaultAdmin();
        repository.Upsert("E001", "Operator A", isActive: true);
        var auth = new PersonnelAuthenticationService(repository);
        Assert.True(auth.Login(PersonnelRepository.AdminEmployeeCode, PersonnelRepository.AdminDefaultPassword).Success);
        var vm = new PersonnelManagementViewModel(repository, auth, () => true);
        var item = vm.Personnel.Single(person => person.EmployeeCode == "E001");

        item.IsActive = false;

        Assert.False(repository.GetByCode("E001")!.IsActive);
        Assert.Contains("已停用", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }
}
