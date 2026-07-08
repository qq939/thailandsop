using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VideoInferenceDemo;

public sealed partial class CameraConfigViewModel : ObservableObject
{
    private readonly string _configPath;
    private readonly CameraProviderRegistry _cameraProviders;
    private readonly IEnvironmentDiagnosticsService _environmentDiagnosticsService;
    private readonly string _baseDirectory;
    private readonly Func<IReadOnlyList<VisionTaskDefinition>> _getAvailableVisionTasks;
    private readonly Action _refreshModelsAction;
    private readonly Action<CameraSettings>? _saveSettings;
    private readonly List<CameraSopProfile> _sopProfiles;
    private IFolderPickerService? _folderPickerService;
    private EnvironmentDiagnosticsStatusInfo _diagnosticsStatusInfo = new(EnvironmentDiagnosticsState.Success, "未运行", "未运行");

    public CameraConfigViewModel(
        string configPath,
        CameraSettings settings,
        CameraProviderRegistry cameraProviders,
        IEnvironmentDiagnosticsService environmentDiagnosticsService,
        IReadOnlyList<VisionTaskDefinition> availableVisionTasks,
        VisionTaskDefinition? initialPrimaryTask,
        string baseDirectory,
        Func<IReadOnlyList<VisionTaskDefinition>> getAvailableVisionTasks,
        Action refreshModelsAction,
        IReadOnlyList<SopProfile> sopProfiles,
        Action<CameraSettings>? saveSettings = null)
    {
        _configPath = configPath;
        _cameraProviders = cameraProviders;
        _environmentDiagnosticsService = environmentDiagnosticsService ?? throw new ArgumentNullException(nameof(environmentDiagnosticsService));
        _baseDirectory = baseDirectory;
        _getAvailableVisionTasks = getAvailableVisionTasks ?? throw new ArgumentNullException(nameof(getAvailableVisionTasks));
        _refreshModelsAction = refreshModelsAction ?? throw new ArgumentNullException(nameof(refreshModelsAction));
        _saveSettings = saveSettings;
        _sopProfiles = (settings?.SopProfiles ?? new List<CameraSopProfile>())
            .Select(CloneSopProfile)
            .ToList();

        CameraProviders = _cameraProviders.DescribeProviders();

        // Populate SOP profile items for dropdown
        var sopItems = (sopProfiles ?? Array.Empty<SopProfile>())
            .Select(p => new SopProfileItem
            {
                Id = p.Id,
                Name = p.Name,
                StepCount = p.Steps?.Count ?? 0
            })
            .ToList();
        foreach (var item in sopItems)
        {
            AvailableSopProfiles.Add(item);
        }

        foreach (var camera in (settings ?? CameraSettings.CreateDefault()).Cameras)
        {
            var vm = new CameraProfileViewModel(camera, _cameraProviders);
            SyncSopProfilesToVm(vm);
            Cameras.Add(vm);
        }

        if (Cameras.Count == 0)
        {
            var vm = new CameraProfileViewModel(CameraProfile.CreateDefault(1), _cameraProviders);
            SyncSopProfilesToVm(vm);
            Cameras.Add(vm);
        }

        selectedCamera = Cameras.FirstOrDefault(camera =>
            string.Equals(camera.Id, settings?.SelectedCameraId, StringComparison.OrdinalIgnoreCase))
            ?? Cameras.FirstOrDefault();

        foreach (var task in availableVisionTasks ?? Array.Empty<VisionTaskDefinition>())
        {
            AvailableVisionTasks.Add(task);
        }
        selectedPrimaryTask = initialPrimaryTask;
        foreach (var camera in Cameras)
        {
            if (string.IsNullOrWhiteSpace(camera.PrimaryTaskId) && initialPrimaryTask != null)
            {
                camera.PrimaryTaskId = initialPrimaryTask.Id;
            }
        }

        CameraDiagnostics.Info(
            "camera-config",
            $"Camera settings view model created. Cameras={Cameras.Count}");

        RefreshTensorRtCacheStatus();
    }

    private void SyncSopProfilesToVm(CameraProfileViewModel vm)
    {
        vm.AvailableSopProfiles.Clear();
        foreach (var item in AvailableSopProfiles)
        {
            vm.AvailableSopProfiles.Add(item);
        }
    }

    public IReadOnlyList<CameraProviderDescriptor> CameraProviders { get; }
    public ObservableCollection<CameraProfileViewModel> Cameras { get; } = new();
    public ObservableCollection<VisionTaskDefinition> AvailableVisionTasks { get; } = new();
    public ObservableCollection<SopProfileItem> AvailableSopProfiles { get; } = new();
    public VisionTaskDefinition? SavedPrimaryTask { get; private set; }
    public string DiagnosticsLogPath => CameraDiagnostics.LogPath;
    public string EnvironmentDiagnosticsLogPath => DesktopEnvironmentDiagnosticsService.ReportPath;
    public string ProviderInitLogPath => OrtSessionFactory.ProviderLogPath;
    public EnvironmentDiagnosticsStatusInfo DiagnosticsStatusInfo => _diagnosticsStatusInfo;
    public bool HasSelectedCamera => SelectedCamera != null;
    public bool HasSelectedPrimaryTask => ResolveSelectedCameraTask() != null;
    public IFolderPickerService? FolderPickerService
    {
        get => _folderPickerService;
        set
        {
            _folderPickerService = value;
            BrowseRecordingRootDirectoryCommand.NotifyCanExecuteChanged();
        }
    }

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private CameraProfileViewModel? selectedCamera;

    [ObservableProperty]
    private string environmentDiagnosticsStatus = "未运行";

    [ObservableProperty]
    private bool isRunningEnvironmentDiagnostics;

    [ObservableProperty]
    private VisionTaskDefinition? selectedPrimaryTask;

    [ObservableProperty]
    private bool isTensorRtReady;

    [ObservableProperty]
    private string cacheBuildStatusText = string.Empty;

    [ObservableProperty]
    private bool isBuildingTensorRtCache;

    private CameraProfileViewModel? _previousSelectedCamera;

    partial void OnSelectedCameraChanged(CameraProfileViewModel? value)
    {
        if (_previousSelectedCamera != null)
        {
            _previousSelectedCamera.PropertyChanged -= OnSelectedCameraPropertyChanged;
        }

        if (value != null)
        {
            value.PropertyChanged += OnSelectedCameraPropertyChanged;
        }
        _previousSelectedCamera = value;

        OnPropertyChanged(nameof(HasSelectedCamera));
        OnPropertyChanged(nameof(HasSelectedPrimaryTask));
        RemoveCameraCommand.NotifyCanExecuteChanged();
        DuplicateCameraCommand.NotifyCanExecuteChanged();
        MoveSelectedCameraUpCommand.NotifyCanExecuteChanged();
        MoveSelectedCameraDownCommand.NotifyCanExecuteChanged();
        RefreshDevicesCommand.NotifyCanExecuteChanged();
        BrowseRecordingRootDirectoryCommand.NotifyCanExecuteChanged();
        BuildSelectedTensorRtCacheCommand.NotifyCanExecuteChanged();
        RefreshTensorRtCacheStatus();
    }

    private void OnSelectedCameraPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CameraProfileViewModel.PrimaryTaskId))
        {
            OnPropertyChanged(nameof(HasSelectedPrimaryTask));
            BuildSelectedTensorRtCacheCommand.NotifyCanExecuteChanged();
            RefreshTensorRtCacheStatus();
        }
    }

    partial void OnIsRunningEnvironmentDiagnosticsChanged(bool value)
    {
        RunEnvironmentDiagnosticsCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedPrimaryTaskChanged(VisionTaskDefinition? value)
    {
        OnPropertyChanged(nameof(HasSelectedPrimaryTask));
        BuildSelectedTensorRtCacheCommand.NotifyCanExecuteChanged();
        RefreshTensorRtCacheStatus();
    }

    partial void OnIsBuildingTensorRtCacheChanged(bool value)
    {
        BuildSelectedTensorRtCacheCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void AddCamera()
    {
        var camera = new CameraProfileViewModel(CameraProfile.CreateDefault(Cameras.Count + 1), _cameraProviders);
        SyncSopProfilesToVm(camera);
        Cameras.Add(camera);
        SelectedCamera = camera;
    }

    [RelayCommand(CanExecute = nameof(CanRemoveCamera))]
    private void RemoveCamera()
    {
        if (SelectedCamera == null || Cameras.Count <= 1)
        {
            return;
        }

        var selected = SelectedCamera;
        var index = Cameras.IndexOf(selected);
        if (index < 0)
        {
            SelectedCamera = Cameras.FirstOrDefault();
            return;
        }

        var replacementIndex = index >= Cameras.Count - 1 ? index - 1 : index + 1;
        SelectedCamera = Cameras[replacementIndex];
        Cameras.RemoveAt(index);
        RemoveCameraCommand.NotifyCanExecuteChanged();
    }

    private bool CanRemoveCamera() => SelectedCamera != null && Cameras.Count > 1;

    [RelayCommand(CanExecute = nameof(CanDuplicateCamera))]
    private void DuplicateCamera()
    {
        if (SelectedCamera == null)
        {
            return;
        }

        var duplicate = new CameraProfileViewModel(SelectedCamera.Duplicate(), _cameraProviders);
        SyncSopProfilesToVm(duplicate);
        var index = Cameras.IndexOf(SelectedCamera);
        if (index < 0)
        {
            Cameras.Add(duplicate);
        }
        else
        {
            Cameras.Insert(index + 1, duplicate);
        }

        SelectedCamera = duplicate;
    }

    private bool CanDuplicateCamera() => SelectedCamera != null;

    [RelayCommand(CanExecute = nameof(CanMoveSelectedCameraUp))]
    private void MoveSelectedCameraUp()
    {
        if (SelectedCamera == null)
        {
            return;
        }

        var index = Cameras.IndexOf(SelectedCamera);
        if (index <= 0)
        {
            return;
        }

        Cameras.Move(index, index - 1);
    }

    private bool CanMoveSelectedCameraUp() => SelectedCamera != null && Cameras.IndexOf(SelectedCamera) > 0;

    [RelayCommand(CanExecute = nameof(CanMoveSelectedCameraDown))]
    private void MoveSelectedCameraDown()
    {
        if (SelectedCamera == null)
        {
            return;
        }

        var index = Cameras.IndexOf(SelectedCamera);
        if (index < 0 || index >= Cameras.Count - 1)
        {
            return;
        }

        Cameras.Move(index, index + 1);
    }

    private bool CanMoveSelectedCameraDown() => SelectedCamera != null && Cameras.IndexOf(SelectedCamera) < Cameras.Count - 1;

    [RelayCommand(CanExecute = nameof(CanRefreshDevices))]
    private async System.Threading.Tasks.Task RefreshDevices()
    {
        if (SelectedCamera == null)
        {
            return;
        }

        await SelectedCamera.RefreshDevicesCommand.ExecuteAsync(null);
    }

    private bool CanRefreshDevices() => SelectedCamera != null;

    [RelayCommand(CanExecute = nameof(CanBrowseRecordingRootDirectory))]
    private void BrowseRecordingRootDirectory()
    {
        if (SelectedCamera == null || FolderPickerService == null)
        {
            return;
        }

        var selectedPath = FolderPickerService.PickFolder(
            "选择录像根目录",
            SelectedCamera.RecordingRootDirectoryText);

        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            SelectedCamera.RecordingRootDirectoryText = selectedPath;
        }
    }

    private bool CanBrowseRecordingRootDirectory() => SelectedCamera != null && FolderPickerService != null;

    [RelayCommand(CanExecute = nameof(CanRunEnvironmentDiagnostics))]
    private async System.Threading.Tasks.Task RunEnvironmentDiagnostics()
    {
        IsRunningEnvironmentDiagnostics = true;
        EnvironmentDiagnosticsStatus = "正在执行环境诊断...";
        ErrorMessage = string.Empty;

        try
        {
            var result = await System.Threading.Tasks.Task.Run(() => _environmentDiagnosticsService.Run());
            _diagnosticsStatusInfo = EnvironmentDiagnosticsStatusInfo.FromResult(result);
            EnvironmentDiagnosticsStatus = _diagnosticsStatusInfo.Detail;
            OnPropertyChanged(nameof(DiagnosticsStatusInfo));
            CameraDiagnostics.Info(
                "camera-config",
                $"Environment diagnostics completed. State={result.State}, ReportPath={result.ReportPath}");
        }
        catch (Exception ex)
        {
            EnvironmentDiagnosticsStatus = $"环境诊断失败：{ex.Message}";
            _diagnosticsStatusInfo = new EnvironmentDiagnosticsStatusInfo(
                EnvironmentDiagnosticsState.Error,
                "环境异常",
                EnvironmentDiagnosticsStatus);
            OnPropertyChanged(nameof(DiagnosticsStatusInfo));
            ErrorMessage = EnvironmentDiagnosticsStatus;
            CameraDiagnostics.Error("camera-config", "Environment diagnostics failed.", ex);
        }
        finally
        {
            IsRunningEnvironmentDiagnostics = false;
        }
    }

    private bool CanRunEnvironmentDiagnostics() => !IsRunningEnvironmentDiagnostics;

    [RelayCommand]
    private void RefreshModels()
    {
        _refreshModelsAction();
        // Sync local task list after refresh
        AvailableVisionTasks.Clear();
        foreach (var task in _getAvailableVisionTasks())
        {
            AvailableVisionTasks.Add(task);
        }

        BuildSelectedTensorRtCacheCommand.NotifyCanExecuteChanged();
        RefreshTensorRtCacheStatus();
    }

    [RelayCommand(CanExecute = nameof(CanBuildSelectedTensorRtCache))]
    private async Task BuildSelectedTensorRtCache()
    {
        var task = ResolveSelectedCameraTask();
        ErrorMessage = string.Empty;
        if (task == null)
        {
            CacheBuildStatusText = "请先选择视觉任务";
            return;
        }

        if (task.RuntimeKind != VisionRuntimeKind.OnnxRuntime)
        {
            CacheBuildStatusText = $"{task.DisplayName} 不使用 TensorRT 缓存";
            return;
        }

        var modelPath = task.Metadata.GetValueOrDefault("modelPath");
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            CacheBuildStatusText = $"{task.DisplayName}: 未找到 ONNX 模型文件";
            return;
        }

        if (!TryGetPositiveInt(task.Metadata, "inputWidth", out var inputWidth) ||
            !TryGetPositiveInt(task.Metadata, "inputHeight", out var inputHeight))
        {
            CacheBuildStatusText = $"{task.DisplayName}: model.json 缺少 inputWidth/inputHeight。";
            return;
        }

        IsBuildingTensorRtCache = true;
        CacheBuildStatusText = $"正在构建 {task.DisplayName}…";

        try
        {
            var cacheDir = Path.Combine(_baseDirectory, "trt-cache", task.Id);
            var result = await Task.Run(() =>
                TensorRtCacheBuilder.BuildCacheAsync(
                    modelPath,
                    cacheDir,
                    inputWidth,
                    inputHeight,
                    fp16: true));

            if (result.Success)
            {
                IsTensorRtReady = true;
                CacheBuildStatusText = $"{task.DisplayName}: TensorRT 引擎缓存构建成功";
            }
            else
            {
                IsTensorRtReady = false;
                CacheBuildStatusText = $"{task.DisplayName}: {result.Message}";
                ErrorMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            IsTensorRtReady = false;
            CacheBuildStatusText = $"{task.DisplayName}: TensorRT 缓存构建失败：{ex.Message}";
            ErrorMessage = CacheBuildStatusText;
            CameraDiagnostics.Error("camera-config", $"Failed to build TensorRT cache for task '{task.Id}'.", ex);
        }
        finally
        {
            IsBuildingTensorRtCache = false;
            if (IsTensorRtReady)
            {
                RefreshTensorRtCacheStatus();
            }
        }
    }

    private static bool TryGetPositiveInt(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        out int value)
    {
        value = 0;
        return metadata.TryGetValue(key, out var raw) &&
               int.TryParse(raw, out value) &&
               value > 0;
    }

    private bool CanBuildSelectedTensorRtCache()
    {
        return !IsBuildingTensorRtCache
            && ResolveSelectedCameraTask()?.RuntimeKind == VisionRuntimeKind.OnnxRuntime;
    }

    public bool TrySave()
    {
        ErrorMessage = string.Empty;

        var profiles = new List<CameraProfile>(Cameras.Count);
        foreach (var camera in Cameras)
        {
            if (!camera.TryBuild(out var profile, out var error))
            {
                ErrorMessage = $"{camera.SidebarTitle}: {error}";
                SelectedCamera = camera;
                return false;
            }

            profiles.Add(profile);
        }

        if (profiles.Count == 0)
        {
            profiles.Add(CameraProfile.CreateDefault(1));
        }

        var settings = new CameraSettings
        {
            Cameras = profiles,
            SopProfiles = _sopProfiles.Select(CloneSopProfile).ToList(),
            SelectedCameraId = SelectedCamera?.Id ?? profiles[0].Id
        };

        try
        {
            if (_saveSettings != null)
            {
                _saveSettings(settings);
            }
            else
            {
                CameraSettingsStorage.Save(_configPath, settings);
            }
            SavedPrimaryTask = ResolveSelectedCameraTask() ?? SelectedPrimaryTask;
            CameraDiagnostics.Info("camera-config", $"Saved camera settings. Cameras={profiles.Count}");
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存相机配置失败：{ex.Message}";
            CameraDiagnostics.Error("camera-config", "Failed to save camera settings.", ex);
            return false;
        }
    }

    private static CameraSopProfile CloneSopProfile(CameraSopProfile profile)
    {
        return new CameraSopProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            Strategy = profile.Strategy,
            FingerprintModuleId = profile.FingerprintModuleId,
            Steps = (profile.Steps ?? new List<CameraSopStep>())
                .Select(step => new CameraSopStep
                {
                    Step = step.Step,
                    Name = step.Name,
                    ActionCode = step.ActionCode,
                    TcnLabel = step.TcnLabel,
                    ExpectedStateCode = step.ExpectedStateCode
                })
                .ToList()
        };
    }

    private void RefreshTensorRtCacheStatus()
    {
        var selectedTask = ResolveSelectedCameraTask();
        var modelId = selectedTask?.Id;
        if (!string.IsNullOrWhiteSpace(modelId))
        {
            var cacheDir = Path.Combine(_baseDirectory, "trt-cache", modelId);
            if (Directory.Exists(cacheDir) &&
                Directory.EnumerateFiles(cacheDir, "*", SearchOption.AllDirectories).Any())
            {
                IsTensorRtReady = true;
                CacheBuildStatusText = "TensorRT 引擎缓存已就绪";
                return;
            }
        }

        IsTensorRtReady = false;
        CacheBuildStatusText = "未检测到 TensorRT 缓存，点击上方按钮构建";
    }

    private VisionTaskDefinition? ResolveSelectedCameraTask()
    {
        var taskId = SelectedCamera?.PrimaryTaskId;
        return string.IsNullOrWhiteSpace(taskId)
            ? SelectedPrimaryTask
            : AvailableVisionTasks.FirstOrDefault(task =>
                  string.Equals(task.Id, taskId, StringComparison.OrdinalIgnoreCase))
              ?? SelectedPrimaryTask;
    }
}
