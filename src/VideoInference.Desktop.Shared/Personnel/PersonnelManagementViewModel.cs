using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewLife.Log;

namespace VideoInferenceDemo;

public sealed partial class PersonnelEditorItem : ObservableObject
{
    [ObservableProperty]
    private string employeeCode = string.Empty;

    [ObservableProperty]
    private string employeeName = string.Empty;

    [ObservableProperty]
    private string passwordText = string.Empty;

    [ObservableProperty]
    private string team = string.Empty;

    [ObservableProperty]
    private string fingerprintIdText = string.Empty;

    [ObservableProperty]
    private bool isActive = true;

    [ObservableProperty]
    private string note = string.Empty;

    public bool ExistsInStore { get; set; }

    public bool IsAdmin => PersonnelRepository.IsAdminCode(EmployeeCode);

    public string PasswordDisplayText => string.IsNullOrEmpty(PasswordText) ? "空密码" : "已设置";

    partial void OnPasswordTextChanged(string value)
    {
        OnPropertyChanged(nameof(PasswordDisplayText));
    }

    public static PersonnelEditorItem FromRecord(PersonnelRecord record)
    {
        return new PersonnelEditorItem
        {
            EmployeeCode = record.EmployeeCode,
            EmployeeName = record.EmployeeName,
            PasswordText = record.PasswordText,
            Team = record.Team,
            IsActive = record.IsActive,
            Note = record.Note,
            ExistsInStore = true
        };
    }
}

public sealed partial class PersonnelManagementViewModel : ObservableObject, IDisposable
{
    private readonly PersonnelRepository _repository;
    private readonly PersonnelAuthenticationService? _authenticationService;
    private readonly Func<bool>? _confirmAdminPassword;
    private readonly Func<PersonnelEditorItem, string?>? _requestPasswordText;
    private readonly Action? _onChanged;
    private readonly List<FingerprintModuleOptions> _fingerprintModules;
    private bool _moduleSelectionGuard;
    private readonly Func<string, Task>? _suspendFingerprintModuleAsync;
    private readonly Func<string, Task>? _resumeFingerprintModuleAsync;
    private bool _activeChangeGuard;
    private bool _disposed;

    public PersonnelManagementViewModel(
        PersonnelRepository repository,
        Action? onChanged = null,
        IReadOnlyList<FingerprintModuleOptions>? fingerprintModules = null,
        Func<string, Task>? suspendFingerprintModuleAsync = null,
        Func<string, Task>? resumeFingerprintModuleAsync = null)
        : this(repository, authenticationService: null, confirmAdminPassword: null, requestPasswordText: null, onChanged, fingerprintModules, suspendFingerprintModuleAsync, resumeFingerprintModuleAsync)
    {
    }

    public PersonnelManagementViewModel(
        PersonnelRepository repository,
        PersonnelAuthenticationService? authenticationService,
        Func<bool>? confirmAdminPassword,
        Func<PersonnelEditorItem, string?>? requestPasswordText = null,
        Action? onChanged = null,
        IReadOnlyList<FingerprintModuleOptions>? fingerprintModules = null,
        Func<string, Task>? suspendFingerprintModuleAsync = null,
        Func<string, Task>? resumeFingerprintModuleAsync = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _authenticationService = authenticationService;
        _confirmAdminPassword = confirmAdminPassword;
        _requestPasswordText = requestPasswordText;
        _onChanged = onChanged;
        _suspendFingerprintModuleAsync = suspendFingerprintModuleAsync;
        _resumeFingerprintModuleAsync = resumeFingerprintModuleAsync;
        _fingerprintModules = (fingerprintModules ?? Array.Empty<FingerprintModuleOptions>())
            .Select(item => item.Normalize())
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToList();
        AvailableModules = new ObservableCollection<FingerprintModuleOptions>(_fingerprintModules);
        if (_authenticationService != null)
        {
            _authenticationService.CurrentSessionChanged += OnCurrentSessionChanged;
        }

        Refresh();
    }

    public ObservableCollection<PersonnelEditorItem> Personnel { get; } = [];
    public ObservableCollection<FingerprintModuleOptions> AvailableModules { get; }

    [ObservableProperty]
    private PersonnelEditorItem? selectedPersonnel;

    [ObservableProperty]
    private string statusText = "就绪";

    [ObservableProperty]
    private bool isEnrolling;

    [ObservableProperty]
    private FingerprintModuleOptions? selectedFingerprintModule;

    public bool HasFingerprintModules => _fingerprintModules.Count > 0;

    public bool CurrentUserIsAdmin => _authenticationService?.CurrentSession?.IsAdmin ?? true;

    public string CurrentUserText => _authenticationService?.CurrentSession?.DisplayText ?? "未接入登录会话";

    partial void OnSelectedFingerprintModuleChanged(FingerprintModuleOptions? value)
    {
        if (_moduleSelectionGuard) return;
        RefreshFingerprintBindings();
        EnrollFingerprintCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Refresh()
    {
        foreach (var item in Personnel)
        {
            item.PropertyChanged -= OnPersonnelItemPropertyChanged;
        }

        var selectedCode = SelectedPersonnel?.EmployeeCode;
        var items = _repository.List(includeInactive: true);
        Personnel.Clear();
        foreach (var record in items)
        {
            var item = PersonnelEditorItem.FromRecord(record);
            item.PropertyChanged += OnPersonnelItemPropertyChanged;
            Personnel.Add(item);
        }

        SelectedPersonnel = Personnel.FirstOrDefault(item =>
                                string.Equals(item.EmployeeCode, selectedCode, StringComparison.OrdinalIgnoreCase))
                            ?? Personnel.FirstOrDefault();
        StatusText = $"已加载 {Personnel.Count} 位员工";

        if (!_moduleSelectionGuard && SelectedFingerprintModule == null && _fingerprintModules.Count > 0)
        {
            _moduleSelectionGuard = true;
            SelectedFingerprintModule = _fingerprintModules[0];
            _moduleSelectionGuard = false;
        }

        RefreshFingerprintBindings();
        NotifyPersonnelCommandsChanged();

        if (_fingerprintModules.Count == 0)
        {
            StatusText = "未配置指纹模块；如需录入指纹，请先在系统设置中添加指纹模块。";
        }
    }

    private void RefreshFingerprintBindings()
    {
        var moduleId = SelectedFingerprintModule?.Id;
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            foreach (var item in Personnel)
            {
                item.FingerprintIdText = string.Empty;
            }

            return;
        }

        foreach (var item in Personnel)
        {
            var binding = _repository.GetFingerprintBinding(item.EmployeeCode, moduleId);
            item.FingerprintIdText = binding?.ToString() ?? string.Empty;
        }
    }

    [RelayCommand]
    private void AddPersonnel()
    {
        if (!RequireAdminAccess("新增人员需要 Admin 权限。"))
        {
            return;
        }

        var item = new PersonnelEditorItem
        {
            IsActive = true
        };
        item.PropertyChanged += OnPersonnelItemPropertyChanged;
        Personnel.Add(item);
        SelectedPersonnel = item;
        StatusText = "已新增一行，请填写工号和姓名后保存；密码可通过“修改密码”设置。";
    }

    [RelayCommand(CanExecute = nameof(CanSaveSelected))]
    private void SaveSelected()
    {
        var selected = SelectedPersonnel;
        if (selected == null) return;

        if (!RequireAdminAccess("保存人员资料需要 Admin 权限。"))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(selected.EmployeeCode))
        {
            StatusText = "工号不能为空。";
            return;
        }

        if (string.IsNullOrWhiteSpace(selected.EmployeeName))
        {
            StatusText = "姓名不能为空。";
            return;
        }

        if (selected.IsAdmin && !selected.IsActive)
        {
            _activeChangeGuard = true;
            try
            {
                selected.IsActive = true;
            }
            finally
            {
                _activeChangeGuard = false;
            }

            StatusText = "Admin 不能停用。";
            return;
        }

        _repository.Upsert(
            selected.EmployeeCode,
            selected.EmployeeName,
            selected.Team,
            selected.IsActive,
            selected.Note,
            fingerprintId: null,
            passwordText: selected.PasswordText);
        StatusText = $"已保存：{selected.EmployeeCode}";
        _onChanged?.Invoke();
        Refresh();
    }

    private bool CanSaveSelected() => SelectedPersonnel != null;

    [RelayCommand(CanExecute = nameof(CanEnableSelected))]
    private void EnableSelected()
    {
        EnablePersonnelCore(SelectedPersonnel);
    }

    private bool CanEnableSelected() => SelectedPersonnel != null;

    [RelayCommand(CanExecute = nameof(CanEnablePersonnel))]
    private void EnablePersonnel(PersonnelEditorItem? item)
    {
        EnablePersonnelCore(item);
    }

    private bool CanEnablePersonnel(PersonnelEditorItem? item) => item != null;

    private void EnablePersonnelCore(PersonnelEditorItem? item)
    {
        if (item == null) return;
        SelectedPersonnel = item;
        if (!RequireAdminAccess("启用人员需要 Admin 权限。"))
        {
            return;
        }

        if (_repository.SetActive(item.EmployeeCode, true))
        {
            StatusText = $"已启用：{item.EmployeeCode}";
            _onChanged?.Invoke();
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDisableSelected))]
    private void DisableSelected()
    {
        DisablePersonnelCore(SelectedPersonnel);
    }

    private bool CanDisableSelected() => SelectedPersonnel != null && !SelectedPersonnel.IsAdmin;

    [RelayCommand(CanExecute = nameof(CanDisablePersonnel))]
    private void DisablePersonnel(PersonnelEditorItem? item)
    {
        DisablePersonnelCore(item);
    }

    private bool CanDisablePersonnel(PersonnelEditorItem? item) => item != null && !item.IsAdmin;

    private void DisablePersonnelCore(PersonnelEditorItem? item)
    {
        if (item == null) return;
        SelectedPersonnel = item;
        if (item.IsAdmin)
        {
            StatusText = "Admin 不能停用。";
            return;
        }

        if (!RequireAdminAccess("停用人员需要 Admin 权限。"))
        {
            return;
        }

        if (_repository.Deactivate(item.EmployeeCode))
        {
            StatusText = $"已停用：{item.EmployeeCode}";
            _onChanged?.Invoke();
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangePasswordSelected))]
    private void ChangePasswordSelected()
    {
        ChangePasswordPersonnelCore(SelectedPersonnel);
    }

    private bool CanChangePasswordSelected() => SelectedPersonnel != null && CanCurrentUserChangePassword(SelectedPersonnel);

    [RelayCommand(CanExecute = nameof(CanChangePasswordPersonnel))]
    private void ChangePasswordPersonnel(PersonnelEditorItem? item)
    {
        ChangePasswordPersonnelCore(item);
    }

    private bool CanChangePasswordPersonnel(PersonnelEditorItem? item) => item != null && CanCurrentUserChangePassword(item);

    private void ChangePasswordPersonnelCore(PersonnelEditorItem? item)
    {
        if (item == null)
        {
            return;
        }

        SelectedPersonnel = item;
        if (!CanCurrentUserChangePassword(item))
        {
            StatusText = "普通用户只能修改自己的密码。";
            return;
        }

        if (string.IsNullOrWhiteSpace(item.EmployeeCode) || !item.ExistsInStore)
        {
            StatusText = "请先保存员工信息，再修改密码。";
            return;
        }

        if (CurrentUserIsAdmin && !IsCurrentUser(item) && !RequireAdminAccess("修改其他用户密码需要 Admin 权限。"))
        {
            return;
        }

        string passwordText;
        if (_requestPasswordText != null)
        {
            var requestedPasswordText = _requestPasswordText(item);
            if (requestedPasswordText == null)
            {
                StatusText = "已取消修改密码。";
                return;
            }

            passwordText = requestedPasswordText;
        }
        else
        {
            passwordText = item.PasswordText;
        }

        if (!_repository.SetPassword(item.EmployeeCode, passwordText))
        {
            StatusText = $"密码修改失败：{item.EmployeeCode}";
            return;
        }

        item.PasswordText = passwordText;
        StatusText = $"已修改密码：{item.EmployeeCode}";
        _onChanged?.Invoke();
        Refresh();
    }

    private bool CanCurrentUserChangePassword(PersonnelEditorItem selected)
    {
        return CurrentUserIsAdmin || IsCurrentUser(selected);
    }

    private bool CanExecuteEnroll(PersonnelEditorItem? item) => !IsEnrolling && item != null;

    partial void OnIsEnrollingChanged(bool value)
    {
        EnrollFingerprintCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanExecuteEnroll))]
    private async Task EnrollFingerprint(PersonnelEditorItem? item)
    {
        if (item == null)
        {
            StatusText = "请先选择一位员工。";
            return;
        }

        if (!RequireAdminAccess("录入指纹需要 Admin 权限。"))
        {
            return;
        }

        var module = SelectedFingerprintModule;
        if (module == null)
        {
            StatusText = "未选择指纹模块，请先在系统设置中添加模块，并在人员页选择模块。";
            return;
        }

        if (string.IsNullOrWhiteSpace(item.EmployeeCode))
        {
            StatusText = "请先填写并保存员工工号，再录入指纹。";
            return;
        }

        if (_repository.GetByCode(item.EmployeeCode) == null)
        {
            StatusText = $"员工 {item.EmployeeCode} 尚未保存，请先点击保存。";
            return;
        }

        IsEnrolling = true;
        await Task.Yield();

        // 暂停指纹监控释放 COM 口，避免与录入冲突
        if (_suspendFingerprintModuleAsync != null)
        {
            await _suspendFingerprintModuleAsync(module.Id);
        }

        try
        {
            var service = new FingerprintEnrollmentService();
            var existingId = _repository.GetFingerprintBinding(item.EmployeeCode, module.Id);

            if (existingId.HasValue)
            {
                StatusText = $"正在重新录入指纹 {existingId.Value}（模块：{module.Name}），请将手指放在扫描仪上...";
                var result = await service.EnrollAsync((byte)existingId.Value, module);
                if (result.Success)
                {
                    _repository.SetFingerprintBinding(item.EmployeeCode, module.Id, existingId.Value);
                    _onChanged?.Invoke();
                    Refresh();
                    StatusText = $"指纹 {existingId.Value}（模块：{module.Name}）重新录入成功。";
                }
                else
                {
                    StatusText = result.DisplayText;
                    await Task.Delay(5000);
                }
            }
            else
            {
                StatusText = $"正在查询模块“{module.Name}”并录入，请将手指放在扫描仪上...";
                var result = await service.EnrollNextAvailableAsync(module);
                if (result.Success)
                {
                    _repository.SetFingerprintBinding(item.EmployeeCode, module.Id, result.FingerprintId);
                    _onChanged?.Invoke();
                    Refresh();
                    StatusText = $"指纹 {result.FingerprintId}（模块：{module.Name}）录入成功，已绑定到 {item.EmployeeCode}。";
                }
                else
                {
                    StatusText = result.DisplayText;
                    await Task.Delay(5000);
                }
            }
        }
        catch (Exception ex)
        {
            XTrace.WriteLine("[Enroll] 指纹录入异常: {0}", ex);
            StatusText = $"录入异常：{ex.Message}";
            await Task.Delay(5000);
        }
        finally
        {
            IsEnrolling = false;
            // 恢复指纹监控，重新打开 COM 口
            if (_resumeFingerprintModuleAsync != null)
            {
                await _resumeFingerprintModuleAsync(module.Id);
            }
        }
    }

    partial void OnSelectedPersonnelChanged(PersonnelEditorItem? value)
    {
        NotifyPersonnelCommandsChanged();
    }

    private void OnPersonnelItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PersonnelEditorItem.EmployeeCode)
            or nameof(PersonnelEditorItem.EmployeeName)
            or nameof(PersonnelEditorItem.PasswordText))
        {
            NotifyPersonnelCommandsChanged();
            return;
        }

        if (e.PropertyName == nameof(PersonnelEditorItem.IsActive) && sender is PersonnelEditorItem item)
        {
            HandleActiveChanged(item);
        }
    }

    private void HandleActiveChanged(PersonnelEditorItem item)
    {
        if (_activeChangeGuard)
        {
            return;
        }

        SelectedPersonnel = item;
        if (item.IsAdmin && !item.IsActive)
        {
            RevertActiveFromStore(item, fallback: true);
            StatusText = "Admin 不能停用。";
            return;
        }

        if (!item.ExistsInStore || string.IsNullOrWhiteSpace(item.EmployeeCode))
        {
            return;
        }

        var previousState = _repository.GetByCode(item.EmployeeCode)?.IsActive ?? true;
        var message = item.IsActive ? "启用人员需要 Admin 权限。" : "停用人员需要 Admin 权限。";
        if (!RequireAdminAccess(message))
        {
            RevertActiveFromStore(item, previousState);
            return;
        }

        var changed = item.IsActive
            ? _repository.SetActive(item.EmployeeCode, true)
            : _repository.Deactivate(item.EmployeeCode);
        if (!changed)
        {
            RevertActiveFromStore(item, previousState);
            StatusText = item.IsActive
                ? $"启用失败：{item.EmployeeCode}"
                : $"停用失败：{item.EmployeeCode}";
            return;
        }

        var status = item.IsActive
            ? $"已启用：{item.EmployeeCode}"
            : $"已停用：{item.EmployeeCode}";
        _onChanged?.Invoke();
        Refresh();
        StatusText = status;
    }

    private void RevertActiveFromStore(PersonnelEditorItem item, bool fallback)
    {
        var storeValue = item.ExistsInStore && !string.IsNullOrWhiteSpace(item.EmployeeCode)
            ? _repository.GetByCode(item.EmployeeCode)?.IsActive ?? fallback
            : fallback;
        _activeChangeGuard = true;
        try
        {
            item.IsActive = storeValue;
        }
        finally
        {
            _activeChangeGuard = false;
        }
    }

    private bool RequireAdminAccess(string message)
    {
        if (!CurrentUserIsAdmin)
        {
            StatusText = message;
            return false;
        }

        if (_confirmAdminPassword != null && !_confirmAdminPassword())
        {
            StatusText = "Admin 密码验证已取消或失败。";
            return false;
        }

        return true;
    }

    private bool IsCurrentUser(PersonnelEditorItem selected)
    {
        return string.Equals(
            _authenticationService?.CurrentSession?.EmployeeCode,
            selected.EmployeeCode,
            StringComparison.OrdinalIgnoreCase);
    }

    private void NotifyPersonnelCommandsChanged()
    {
        OnPropertyChanged(nameof(CurrentUserIsAdmin));
        OnPropertyChanged(nameof(CurrentUserText));
        SaveSelectedCommand.NotifyCanExecuteChanged();
        EnableSelectedCommand.NotifyCanExecuteChanged();
        EnablePersonnelCommand.NotifyCanExecuteChanged();
        DisableSelectedCommand.NotifyCanExecuteChanged();
        DisablePersonnelCommand.NotifyCanExecuteChanged();
        ChangePasswordSelectedCommand.NotifyCanExecuteChanged();
        ChangePasswordPersonnelCommand.NotifyCanExecuteChanged();
        EnrollFingerprintCommand.NotifyCanExecuteChanged();
    }

    private void OnCurrentSessionChanged(object? sender, EventArgs e)
    {
        NotifyPersonnelCommandsChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_authenticationService != null)
        {
            _authenticationService.CurrentSessionChanged -= OnCurrentSessionChanged;
        }

        _disposed = true;
    }
}
