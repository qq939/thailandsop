using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageBox;
using OpenCvSharp;
using VideoInferenceDemo;
using VideoInferenceDemo.ImageInspection.Runtime;
using VideoInferenceDemo.ImageInspection.Services;

namespace VideoInferenceDemo.ImageInspection;

[SupportedOSPlatform("windows7.0")]
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const string DefaultWindowTitle = "\u56fe\u50cf\u68c0\u6d4b\u5de5\u4f5c\u53f0";
    private const string DefaultHomeTitle = "\u56fe\u50cf\u68c0\u6d4b\u5de5\u4f5c\u53f0";
    private readonly IImageInspectionDialogService _dialogs;
    private readonly DesktopNativeRuntimeService _nativeRuntimeService;
    private readonly PersonnelRepository _personnelRepository;
    private readonly PersonnelAuthenticationService _authenticationService;
    private readonly WorkspaceDatabasePaths _databasePaths;
    private readonly InspectionRuntimeService _runtime;
    private readonly InspectionActionRegistry _actionRegistry;
    private readonly IProductPresenceInspectionGate _productPresenceGate;
    private readonly InspectionPlcTriggerController _plcTriggerController;
    private readonly InspectionResultStorageService _resultStorageService;
    private RetentionCleanupService? _retentionCleanupService;
    private bool _isLoadingRuntimeContext;
    private bool _isSynchronizingWorkspaceTab;

    public MainViewModel(
        IImageInspectionDialogService dialogs,
        CameraProviderRegistry cameraProviders,
        PersonnelAuthenticationService? authenticationService = null,
        WorkspaceDatabasePaths? databasePaths = null)
    {
        var branding = AppConfigStorage.LoadBranding(
            AppContext.BaseDirectory,
            AppBrandingKeys.ImageInspection,
            DefaultWindowTitle,
            DefaultHomeTitle);
        WindowTitle = branding.WindowTitle;
        HomeTitle = branding.HomeTitle;

        _nativeRuntimeService = new DesktopNativeRuntimeService();
        _nativeRuntimeService.Initialize();

        _dialogs = dialogs;
        _databasePaths = databasePaths ?? (DbSession.IsInitialized
            ? WorkspaceDatabaseBootstrap.ResolvePaths(AppContext.BaseDirectory)
            : WorkspaceDatabaseBootstrap.Initialize(AppContext.BaseDirectory));
        _personnelRepository = new PersonnelRepository(_databasePaths.ConfigDbPath);
        _personnelRepository.EnsureDefaultAdmin();
        _authenticationService = authenticationService ?? new PersonnelAuthenticationService(_personnelRepository);
        _authenticationService.CurrentSessionChanged += OnAuthenticationSessionChanged;
        _actionRegistry = InspectionActionRegistry.CreateDefault(
            InspectionConfigPaths.RecipeCatalogPath,
            InspectionConfigPaths.ModelSettingsPath);
        _productPresenceGate = new ProductPresenceInspectionGate(InspectionConfigPaths.ModelSettingsPath);
        _runtime = new InspectionRuntimeService(
            cameraProviders,
            _actionRegistry,
            CreateCurrentOperatorSnapshot,
            _productPresenceGate);
        _runtime.FrameProcessed += OnRuntimeFrameProcessed;
        _runtime.TaskFailed += OnRuntimeTaskFailed;
        _resultStorageService = new InspectionResultStorageService(LoadResultStorageOptions);
        _resultStorageService.StatusChanged += OnResultStorageStatusChanged;
        _plcTriggerController = new InspectionPlcTriggerController(
            ResolveRunningTaskForPlc,
            TriggerTaskForPlcAsync);
        _plcTriggerController.StatusChanged += OnPlcTriggerStatusChanged;
        RestartPlcTriggerController();
        RestartRetentionCleanupService();
    }

    public string WindowTitle { get; }

    public string HomeTitle { get; }

    [ObservableProperty] private InspectionWorkspaceTabViewModel? selectedWorkspaceTab;
    [ObservableProperty] private InspectionCameraSessionViewModel? activeCameraSession;
    [ObservableProperty] private InspectionTaskSessionViewModel? activeTaskSession;
    [ObservableProperty] private InspectionWorkspaceSidePanelViewModel? activeSidePanel;
    [ObservableProperty] private string runtimeProductModel = "A100";
    [ObservableProperty] private string runtimePositionNo = "P01";
    [ObservableProperty] private string runtimeStatusText = "全部停止";

    public bool IsLoggedIn => _authenticationService.IsLoggedIn;

    public string CurrentLoginDisplayText => _authenticationService.CurrentSession?.DisplayText ?? "未登录";

    public ObservableCollection<InspectionCameraSessionViewModel> CameraSessions { get; } = [];

    public ObservableCollection<InspectionTaskSessionViewModel> TaskSessions { get; } = [];

    public ObservableCollection<InspectionWorkspaceTabViewModel> WorkspaceTabs { get; } = [];

    public int OverviewColumns
    {
        get
        {
            var count = CameraSessions.Count;
            return count switch
            {
                <= 1 => 1,
                <= 4 => 2,
                _ => 4
            };
        }
    }

    public static MainViewModel CreateRuntime(
        IImageInspectionDialogService dialogs,
        CameraProviderRegistry cameraProviders,
        PersonnelAuthenticationService? authenticationService = null,
        WorkspaceDatabasePaths? databasePaths = null)
    {
        var viewModel = new MainViewModel(dialogs, cameraProviders, authenticationService, databasePaths);
        viewModel.ReloadWorkspace();
        return viewModel;
    }

    partial void OnRuntimeProductModelChanged(string value)
    {
        if (!_isLoadingRuntimeContext)
        {
            ApplyRuntimeRecipeContext();
        }
    }

    partial void OnRuntimePositionNoChanged(string value)
    {
        if (!_isLoadingRuntimeContext)
        {
            ApplyRuntimeRecipeContext();
        }
    }

    partial void OnSelectedWorkspaceTabChanged(InspectionWorkspaceTabViewModel? value)
    {
        if (_isSynchronizingWorkspaceTab)
        {
            return;
        }

        switch (value?.Kind)
        {
            case InspectionWorkspaceKind.Camera:
                SetActiveCamera(value.Camera, syncWorkspaceTab: false);
                break;
            case InspectionWorkspaceKind.Task:
                SetActiveTask(value.Task, value.Task?.SelectedCamera);
                break;
            default:
                SetActiveTask(TaskSessions.FirstOrDefault(), ActiveCameraSession ?? CameraSessions.FirstOrDefault());
                ActiveSidePanel = InspectionWorkspaceSidePanelViewModel.CreateOverview(TaskSessions, CameraSessions);
                break;
        }
    }

    [RelayCommand]
    private async Task StartTask()
    {
        if (ActiveTaskSession == null)
        {
            return;
        }

        if (!EnsureLoggedIn())
        {
            return;
        }

        try
        {
            ActiveTaskSession.StatusText = "启动中";
            ActiveTaskSession.SummaryMessage = "正在启动任务...";
            RefreshRuntimeStatusText();
            await _runtime.StartTaskAsync(ActiveTaskSession);
            ApplyCurrentPlcStatusToTask(ActiveTaskSession);
            RefreshRuntimeStatusText();
            ActiveSidePanel = InspectionWorkspaceSidePanelViewModel.CreateTask(ActiveTaskSession);
        }
        catch (Exception ex)
        {
            ActiveTaskSession.StatusText = "启动失败";
            ActiveTaskSession.SummaryMessage = $"启动任务失败: {ex.Message}";
            RefreshRuntimeStatusText();
            ActiveSidePanel = InspectionWorkspaceSidePanelViewModel.CreateTask(ActiveTaskSession);
        }
    }

    [RelayCommand]
    private void StopTask()
    {
        if (ActiveTaskSession == null)
        {
            return;
        }

        ActiveTaskSession.StatusText = "停止中";
        RefreshRuntimeStatusText();
        _runtime.StopTask(ActiveTaskSession);
        RefreshRuntimeStatusText();
        ActiveSidePanel = InspectionWorkspaceSidePanelViewModel.CreateTask(ActiveTaskSession);
    }

    [RelayCommand]
    private async Task StartAllTasks()
    {
        if (!EnsureLoggedIn())
        {
            return;
        }

        foreach (var task in TaskSessions)
        {
            if (!task.IsRunning)
            {
                try
                {
                    task.StatusText = "启动中";
                    task.SummaryMessage = "正在启动任务...";
                    RefreshRuntimeStatusText();
                    await _runtime.StartTaskAsync(task);
                    ApplyCurrentPlcStatusToTask(task);
                    RefreshRuntimeStatusText();
                }
                catch (Exception ex)
                {
                    task.StatusText = "启动失败";
                    task.SummaryMessage = $"启动任务失败: {ex.Message}";
                    RefreshRuntimeStatusText();
                }
            }
        }

        ActiveSidePanel = InspectionWorkspaceSidePanelViewModel.CreateOverview(TaskSessions, CameraSessions);
    }

    [RelayCommand]
    private void StopAllTasks()
    {
        foreach (var task in TaskSessions.Where(task => task.IsRunning).ToArray())
        {
            task.StatusText = "停止中";
            RefreshRuntimeStatusText();
            _runtime.StopTask(task);
        }

        RefreshRuntimeStatusText();
        ActiveSidePanel = InspectionWorkspaceSidePanelViewModel.CreateOverview(TaskSessions, CameraSessions);
    }

    [RelayCommand]
    private void ManualTrigger()
    {
        if (ActiveTaskSession == null)
        {
            return;
        }

        if (!EnsureLoggedIn())
        {
            return;
        }

        if (!ActiveTaskSession.CanManualTrigger)
        {
            ActiveTaskSession.SummaryMessage = "相机回调任务等待相机硬触发，不支持手动触发命令。";
            ActiveSidePanel = InspectionWorkspaceSidePanelViewModel.CreateTask(ActiveTaskSession);
            return;
        }

        if (!_runtime.TryTrigger(ActiveTaskSession))
        {
            ActiveTaskSession.SummaryMessage = "请先启动任务，再手动触发。";
            ActiveSidePanel = InspectionWorkspaceSidePanelViewModel.CreateTask(ActiveTaskSession);
        }
    }

    private InspectionTaskSessionViewModel? ResolveRunningTaskForPlc(string stationId, string taskId)
    {
        if (!_authenticationService.IsLoggedIn)
        {
            return null;
        }

        return TaskSessions.FirstOrDefault(task =>
            task.IsRunning &&
            task.TriggerMode == InspectionTaskTriggerMode.TriggerCommand &&
            (string.IsNullOrWhiteSpace(taskId) ||
             string.Equals(task.Name, taskId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(task.RecipeTaskId, taskId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(task.Id, taskId, StringComparison.OrdinalIgnoreCase)));
    }

    private Task<InspectionRuntimeTaskResult> TriggerTaskForPlcAsync(InspectionTaskSessionViewModel task)
    {
        if (!_authenticationService.IsLoggedIn)
        {
            return Task.FromResult(InspectionRuntimeTaskResult.NotStarted(task.Id));
        }

        return _runtime.TryTrigger(task, "plc", out var completion)
            ? completion
            : Task.FromResult(InspectionRuntimeTaskResult.NotStarted(task.Id));
    }

    [RelayCommand]
    private async Task InspectOpenedImage(InspectionCameraSessionViewModel? camera)
    {
        var task = ResolveTaskForCamera(camera);
        if (task == null || camera == null)
        {
            return;
        }

        if (!EnsureLoggedIn())
        {
            return;
        }

        var imagePath = camera.FrameImagePath;
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            task.SummaryMessage = $"请先为 {camera.Name} 打开一张图片，再执行图片检测。";
            SetActiveTask(task, camera);
            return;
        }

        SetActiveTask(task, camera);
        await Task.Run(() => InspectImageFile(task, camera, imagePath));
    }

    [RelayCommand]
    private void OpenCameraSettings()
    {
        _dialogs.ShowCameraSettings();
        ReloadWorkspace();
    }

    [RelayCommand]
    private void OpenTaskSettings()
    {
        _dialogs.ShowTaskSettings();
        ReloadWorkspace();
    }

    [RelayCommand]
    private void OpenRoiSettings()
    {
        if (ActiveTaskSession == null)
        {
            return;
        }

        var cameras = ActiveTaskSession.Cameras
            .Select(camera => camera.Profile)
            .ToArray();
        if (cameras.Length == 0)
        {
            ActiveTaskSession.SummaryMessage = "请先在任务设置中绑定相机，再配置 ROI。";
            return;
        }

        if (_dialogs.ShowRoiSettings(
                ActiveTaskSession.ProductModel,
                ActiveTaskSession.RecipeTaskId,
                ActiveTaskSession.PositionNo,
                cameras))
        {
            ReloadRecipeContext(ActiveTaskSession);
        }
    }

    [RelayCommand]
    private void OpenParameterSettings()
    {
        if (_dialogs.ShowParameterSettings())
        {
            RestartRetentionCleanupService();
            RestartPlcTriggerController();
        }
    }

    [RelayCommand]
    private void OpenPersonnelManagement()
    {
        _dialogs.ShowPersonnelManagement(new PersonnelManagementViewModel(
            _personnelRepository,
            _authenticationService,
            () => _dialogs.ConfirmAdminPassword(_personnelRepository),
            _dialogs.RequestPersonnelPassword));
    }

    [RelayCommand]
    private void Login()
    {
        if (!_dialogs.ShowLogin(_authenticationService))
        {
            return;
        }

        RuntimeStatusText = $"已登录：{CurrentLoginDisplayText}";
        NotifyLoginPresentation();
    }

    [RelayCommand]
    private void Logout()
    {
        StopAllTasks();
        _authenticationService.Logout();
        RuntimeStatusText = "已注销";
        NotifyLoginPresentation();
    }

    [RelayCommand]
    private void OpenImage(InspectionCameraSessionViewModel? camera)
    {
        camera ??= ActiveCameraSession;
        if (camera == null)
        {
            return;
        }

        var imagePath = _dialogs.PickImageFile();
        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            LoadImage(camera, imagePath);
            var task = ResolveTaskForCamera(camera);
            if (task != null)
            {
                SetActiveTask(task, camera);
            }
        }
    }

    [RelayCommand]
    private void UseRecipeReferenceImage()
    {
        if (ActiveTaskSession == null || ActiveCameraSession == null)
        {
            return;
        }

        var catalog = InspectionRecipeCatalogStorage.Load(InspectionConfigPaths.RecipeCatalogPath);
        var recipe = catalog.GetOrCreate(ActiveTaskSession.ProductModel, ActiveTaskSession.RecipeTaskId, ActiveTaskSession.PositionNo);
        InspectionRecipeCameraBinding.NormalizeForCameraIds(recipe, ActiveTaskSession.Cameras.Select(camera => camera.Id).ToArray());
        var path = InspectionRecipeCameraBinding.GetReferenceImagePath(recipe, ActiveCameraSession.Id);
        LoadImageForActiveCamera(path);
    }

    [RelayCommand]
    private void SelectCamera(InspectionCameraSessionViewModel? camera)
    {
        if (camera != null)
        {
            SetActiveCamera(camera);
        }
    }

    private InspectionTaskSessionViewModel? ResolveTaskForCamera(InspectionCameraSessionViewModel? camera)
    {
        if (camera == null)
        {
            return ActiveTaskSession;
        }

        if (ActiveTaskSession?.Cameras.Contains(camera) == true)
        {
            return ActiveTaskSession;
        }

        return TaskSessions.FirstOrDefault(task => task.Cameras.Contains(camera));
    }

    private void ReloadWorkspace()
    {
        foreach (var task in TaskSessions.Where(task => task.IsRunning).ToArray())
        {
            _runtime.StopTask(task);
        }

        var cameraSettings = InspectionCameraSettingsStorage.Load(InspectionConfigPaths.CameraSettingsPath);
        var cameraProfiles = cameraSettings.Cameras.Count == 0
            ? [InspectionCameraProfile.CreateDefault(1)]
            : cameraSettings.Cameras;

        CameraSessions.Clear();
        for (var index = 0; index < cameraProfiles.Count; index++)
        {
            CameraSessions.Add(new InspectionCameraSessionViewModel(cameraProfiles[index], index + 1));
        }

        BuildTaskSessions();
        BuildWorkspaceTabs();

        OnPropertyChanged(nameof(OverviewColumns));

        SelectedWorkspaceTab = WorkspaceTabs.FirstOrDefault();
        ActiveSidePanel = InspectionWorkspaceSidePanelViewModel.CreateOverview(TaskSessions, CameraSessions);
        RefreshRuntimeStatusText();
        RestartRetentionCleanupService();
    }

    private void RestartRetentionCleanupService()
    {
        _retentionCleanupService?.Dispose();
        var parameterSettings = InspectionParameterSettingsStorage.Load(InspectionConfigPaths.ParameterSettingsPath);
        var cameraSettings = InspectionCameraSettingsStorage.Load(InspectionConfigPaths.CameraSettingsPath);
        var imageRoots = cameraSettings.Cameras
            .Select(camera => camera.ImageSaveDirectory)
            .Append("InspectionImages")
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _retentionCleanupService = new RetentionCleanupService(new RetentionCleanupOptions
        {
            ResultsDirectory = Path.Combine(AppContext.BaseDirectory, "results"),
            RecordingDirectories = ["Recordings"],
            InspectionImageDirectories = imageRoots,
            RetentionDays = parameterSettings.RetentionDays,
            EnableAutoCleanup = parameterSettings.EnableAutoCleanup
        });
        _retentionCleanupService.Start();
    }

    private void RestartPlcTriggerController()
    {
        var parameterSettings = InspectionParameterSettingsStorage.Load(InspectionConfigPaths.ParameterSettingsPath);
        var plcTrigger = parameterSettings.PlcTrigger.Normalize();
        plcTrigger.SlaveAddress = 1;
        plcTrigger.StationId = string.Empty;
        _plcTriggerController.Start(plcTrigger);
        foreach (var task in TaskSessions)
        {
            ApplyCurrentPlcStatusToTask(task);
        }
    }

    private void BuildTaskSessions()
    {
        var catalog = InspectionRecipeCatalogStorage.Load(InspectionConfigPaths.RecipeCatalogPath);
        var seed = catalog.Recipes.FirstOrDefault() ?? catalog.GetOrCreate("A100", "task-main", "P01");
        _isLoadingRuntimeContext = true;
        RuntimeProductModel = string.IsNullOrWhiteSpace(seed.ProductModel) ? "A100" : seed.ProductModel.Trim();
        RuntimePositionNo = string.IsNullOrWhiteSpace(seed.PositionNo) ? "P01" : seed.PositionNo.Trim();
        _isLoadingRuntimeContext = false;
        var cameraSettings = InspectionCameraSettingsStorage.Load(InspectionConfigPaths.CameraSettingsPath);
        var taskSettings = InspectionTaskSettingsStorage.Load(
            InspectionConfigPaths.TaskSettingsPath,
            cameraSettings.Cameras,
            seed,
            out var triggerModesAdjusted);
        if (triggerModesAdjusted)
        {
            InspectionTaskSettingsStorage.Save(
                InspectionConfigPaths.TaskSettingsPath,
                taskSettings,
                cameraSettings.Cameras,
                seed);
            CameraDiagnostics.Info(
                "inspection-task-config",
                "Adjusted task trigger mode compatibility and saved inspection task settings.");
        }

        var definitions = taskSettings.Definitions.ToDictionary(definition => definition.Id, StringComparer.OrdinalIgnoreCase);

        TaskSessions.Clear();
        foreach (var instance in taskSettings.Instances.Where(instance => instance.Enabled))
        {
            if (!definitions.TryGetValue(instance.DefinitionId, out var definition) || !definition.Enabled)
            {
                continue;
            }

            var task = new InspectionTaskSessionViewModel(
                instance.Id,
                string.IsNullOrWhiteSpace(instance.Name) ? definition.Name : instance.Name,
                definition.Id,
                RuntimeProductModel,
                RuntimePositionNo,
                instance.StationId,
                definition.ActionType,
                definition.Id,
                instance.TriggerMode);

            var selectedCameraIds = instance.CameraIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var camera in CameraSessions.Where(camera => selectedCameraIds.Contains(camera.Id)))
            {
                task.Cameras.Add(camera);
            }

            task.SelectedCamera = task.Cameras.FirstOrDefault();
            ApplyCurrentPlcStatusToTask(task);
            TaskSessions.Add(task);
            ReloadRecipeContext(task);
            RefreshCameraTaskDisplay(task);
        }

        if (TaskSessions.Count == 0)
        {
            var fallbackTaskId = string.IsNullOrWhiteSpace(seed.TaskId) ? "appearance-check" : seed.TaskId.Trim();
            var fallbackTask = new InspectionTaskSessionViewModel(
                fallbackTaskId,
                string.IsNullOrWhiteSpace(seed.TaskName) ? "检测任务 1" : seed.TaskName.Trim(),
                fallbackTaskId,
                RuntimeProductModel,
                RuntimePositionNo,
                "station-1",
                InspectionActionTypes.RoiInspection,
                "appearance-check",
                InspectionTaskTriggerCompatibility.ResolveDefaultTriggerMode(CameraSessions.Select(camera => camera.Profile).ToList()));
            foreach (var camera in CameraSessions)
            {
                fallbackTask.Cameras.Add(camera);
            }

            fallbackTask.SelectedCamera = fallbackTask.Cameras.FirstOrDefault();
            ApplyCurrentPlcStatusToTask(fallbackTask);
            TaskSessions.Add(fallbackTask);
            ReloadRecipeContext(fallbackTask);
            RefreshCameraTaskDisplay(fallbackTask);
        }
    }

    private void BuildWorkspaceTabs()
    {
        WorkspaceTabs.Clear();
        WorkspaceTabs.Add(InspectionWorkspaceTabViewModel.CreateOverview());

        foreach (var camera in CameraSessions)
        {
            WorkspaceTabs.Add(InspectionWorkspaceTabViewModel.CreateCamera(camera));
        }
    }

    private void SetActiveCamera(InspectionCameraSessionViewModel? camera, bool syncWorkspaceTab = true)
    {
        if (camera == null)
        {
            return;
        }

        if (syncWorkspaceTab)
        {
            var cameraTab = WorkspaceTabs.FirstOrDefault(tab =>
                tab.Kind == InspectionWorkspaceKind.Camera &&
                ReferenceEquals(tab.Camera, camera));
            if (cameraTab != null && !ReferenceEquals(SelectedWorkspaceTab, cameraTab))
            {
                _isSynchronizingWorkspaceTab = true;
                try
                {
                    SelectedWorkspaceTab = cameraTab;
                }
                finally
                {
                    _isSynchronizingWorkspaceTab = false;
                }
            }
        }

        foreach (var item in CameraSessions)
        {
            item.IsSelected = ReferenceEquals(item, camera);
        }

        ActiveCameraSession = camera;
        var task = TaskSessions.FirstOrDefault(item => item.Cameras.Contains(camera)) ?? ActiveTaskSession;
        SetActiveTask(task, camera);
    }

    private void SetActiveTask(InspectionTaskSessionViewModel? task, InspectionCameraSessionViewModel? preferredCamera)
    {
        ActiveTaskSession = task;
        if (task == null)
        {
            ActiveCameraSession = null;
            ActiveSidePanel = InspectionWorkspaceSidePanelViewModel.CreateTask(null);
            return;
        }

        var camera = preferredCamera != null && task.Cameras.Contains(preferredCamera)
            ? preferredCamera
            : task.SelectedCamera ?? task.Cameras.FirstOrDefault();

        task.SelectedCamera = camera;
        if (camera != null)
        {
            foreach (var item in CameraSessions)
            {
                item.IsSelected = ReferenceEquals(item, camera);
            }

            ActiveCameraSession = camera;
        }

        ActiveSidePanel = InspectionWorkspaceSidePanelViewModel.CreateTask(task);
    }

    private void ReloadRecipeContext(InspectionTaskSessionViewModel task)
    {
        task.ProductModel = NormalizeRecipePart(RuntimeProductModel, "A100");
        task.PositionNo = NormalizeRecipePart(RuntimePositionNo, "P01");
        RefreshCameraTaskDisplay(task);

        var catalog = InspectionRecipeCatalogStorage.Load(InspectionConfigPaths.RecipeCatalogPath);
        var recipe = catalog.GetOrCreate(task.ProductModel, task.RecipeTaskId, task.PositionNo);
        var taskCameraIds = task.Cameras.Select(camera => camera.Id).ToArray();
        InspectionRecipeCameraBinding.NormalizeForCameraIds(recipe, taskCameraIds, out var recipeChanged);
        if (recipeChanged)
        {
            InspectionRecipeCatalogStorage.Save(InspectionConfigPaths.RecipeCatalogPath, catalog);
        }

        task.ReferenceImagePath = task.Cameras.Count == 0
            ? recipe.ReferenceImagePath
            : InspectionRecipeCameraBinding.GetReferenceImagePath(recipe, task.Cameras[0].Id);
        task.SummaryMessage = $"当前配方包含 {recipe.Rois.Count} 个 ROI，{recipe.ModelBindings.Count} 个模型绑定。";

        foreach (var camera in task.Cameras)
        {
            var referenceImagePath = InspectionRecipeCameraBinding.GetReferenceImagePath(recipe, camera.Id);
            camera.RoiItems.Clear();
            foreach (var roi in recipe.Rois
                         .Where(roi => string.Equals(roi.CameraId, camera.Id, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(roi => roi.SortOrder))
            {
                camera.RoiItems.Add(CreateRoiItem(roi));
            }

            camera.SelectedRoi = camera.RoiItems.FirstOrDefault();
            if (camera.FrameImage == null)
            {
                LoadImage(camera, referenceImagePath);
            }
        }
    }

    private static void RefreshCameraTaskDisplay(InspectionTaskSessionViewModel task)
    {
        var taskText = $"Task: {task.RecipeTaskId} / Action: {task.ActionType}";
        var recipeText = $"Product: {task.ProductModel} / Position: {task.PositionNo}";
        foreach (var camera in task.Cameras)
        {
            camera.TaskDisplayText = taskText;
            camera.RecipeDisplayText = recipeText;
        }
    }

    private void OnRuntimeFrameProcessed(InspectionRuntimeFrameResult frame)
    {
        EnqueueInspectionResult(frame);
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            frame.Camera.ResultOverlayItems.Clear();
            frame.Camera.FrameImage = frame.FrameImage;
            frame.Camera.StatusText = frame.StatusText;
            frame.Task.TimingText = frame.Timing.Summary;
            ApplyResultOverlays(frame);
            ApplyInspectionResult(frame.Task, frame.Camera, frame.Result);
        });
    }

    private void EnqueueInspectionResult(InspectionRuntimeFrameResult frame)
    {
        _resultStorageService.TryEnqueue(new InspectionResultStorageItem(
            frame.Result,
            frame.ImageWidth,
            frame.ImageHeight,
            frame.ImagePath,
            frame.RoiImagePaths));
    }

    private InspectionResultStorageOptions LoadResultStorageOptions()
    {
        return InspectionParameterSettingsStorage
            .Load(InspectionConfigPaths.ParameterSettingsPath)
            .ToResultStorageOptions();
    }

    private void OnResultStorageStatusChanged(InspectionResultStorageStatus status)
    {
        if (status.Success && !status.UsedFallback)
        {
            return;
        }

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            RuntimeStatusText = status.Message;
        });
    }

    private void OnPlcTriggerStatusChanged(object? sender, InspectionPlcTriggerStatus status)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var matched = false;
            foreach (var task in TaskSessions.Where(task => IsPlcStatusForTask(status, task)))
            {
                ApplyPlcStatusToTask(task, status);
                matched = true;
            }

            if (!matched && status.Kind != InspectionTriggerStatusKind.Disabled)
            {
                RuntimeStatusText = $"PLC\u89e6\u53d1\u72b6\u6001: {status.Message}";
            }
        });
    }

    private void ApplyCurrentPlcStatusToTask(InspectionTaskSessionViewModel task)
    {
        var status = _plcTriggerController.CurrentStatus;
        if (IsPlcStatusForTask(status, task))
        {
            ApplyPlcStatusToTask(task, status);
        }
    }

    private static void ApplyPlcStatusToTask(
        InspectionTaskSessionViewModel task,
        InspectionPlcTriggerStatus status)
    {
        task.UpdateTriggerStatus(status.Kind, status.Message);
    }

    private static bool IsPlcStatusForTask(
        InspectionPlcTriggerStatus status,
        InspectionTaskSessionViewModel task)
    {
        if (status.Kind == InspectionTriggerStatusKind.Disabled)
        {
            return true;
        }

        if (task.TriggerMode != InspectionTaskTriggerMode.TriggerCommand)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(status.TaskInstanceId))
        {
            return string.Equals(task.Id, status.TaskInstanceId, StringComparison.OrdinalIgnoreCase);
        }

        return string.IsNullOrWhiteSpace(status.TaskId) ||
               string.Equals(task.Name, status.TaskId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(task.RecipeTaskId, status.TaskId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(task.Id, status.TaskId, StringComparison.OrdinalIgnoreCase);
    }

    private void OnRuntimeTaskFailed(InspectionTaskSessionViewModel task, string message)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            task.StatusText = "运行异常";
            task.SummaryMessage = $"任务运行失败: {message}";
            RefreshRuntimeStatusText();
            ActiveSidePanel = InspectionWorkspaceSidePanelViewModel.CreateTask(task);
        });
    }

    private void InspectImageFile(
        InspectionTaskSessionViewModel task,
        InspectionCameraSessionViewModel camera,
        string imagePath)
    {
        try
        {
            var totalWatch = Stopwatch.StartNew();
            var loadWatch = Stopwatch.StartNew();
            using var image = Cv2.ImRead(imagePath, ImreadModes.Color);
            loadWatch.Stop();
            if (image.Empty())
            {
                throw new InvalidOperationException($"无法读取图片: {imagePath}");
            }
            var imageWidth = image.Width;
            var imageHeight = image.Height;

            var productModel = NormalizeRecipePart(RuntimeProductModel, task.ProductModel);
            var positionNo = NormalizeRecipePart(RuntimePositionNo, task.PositionNo);
            var action = _actionRegistry.Resolve(task.ActionType);
            var actionWatch = Stopwatch.StartNew();
            var triggerTime = DateTimeOffset.Now;
            var productPresence = AttachImageProductPresenceMetadata(_productPresenceGate.Evaluate(image), camera.Id);
            InspectionCycleResult result;
            if (productPresence.ShouldSkipInspection)
            {
                result = CreateProductPresenceSkippedImageResult(
                    task,
                    camera,
                    productModel,
                    positionNo,
                    triggerTime,
                    productPresence);
            }
            else
            {
                result = action.Execute(new InspectionRequest
                {
                    OriginalImage = image,
                    ProductModel = productModel,
                    TaskId = task.RecipeTaskId,
                    PositionNo = positionNo,
                    StationId = task.StationId,
                    TaskInstanceId = task.Id,
                    CameraId = camera.Id,
                    ActionType = task.ActionType,
                    TriggerId = $"image-{triggerTime:yyyyMMddHHmmssfff}",
                    TriggerTime = triggerTime,
                    Operator = CreateCurrentOperatorSnapshot()
                });
                result = AttachProductPresenceResult(result, productPresence);
            }

            actionWatch.Stop();

            var postprocessWatch = Stopwatch.StartNew();
            var frameImage = InspectionRuntimeService.CreateDisplayImageSource(image, result);
            postprocessWatch.Stop();
            totalWatch.Stop();

            var timing = new InspectionRuntimeTiming(
                loadWatch.Elapsed.TotalMilliseconds,
                actionWatch.Elapsed.TotalMilliseconds,
                postprocessWatch.Elapsed.TotalMilliseconds,
                totalWatch.Elapsed.TotalMilliseconds);

            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                camera.ResultOverlayItems.Clear();
                camera.FrameImage = frameImage;
                camera.FrameImagePath = imagePath;
                task.TimingText = timing.Summary;
                var frame = new InspectionRuntimeFrameResult(
                    task,
                    camera,
                    result,
                    frameImage,
                    imageWidth,
                    imageHeight,
                    camera.StatusText,
                    timing,
                    0,
                    null,
                    new Dictionary<string, string>());
                EnqueueInspectionResult(frame);
                ApplyResultOverlays(frame);
                ApplyInspectionResult(task, camera, result, includeAllTaskCameras: false);
                camera.StatusText = $"图片检测: {Path.GetFileName(imagePath)}";
            });
        }
        catch (Exception ex)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                task.StatusText = "图片检测失败";
                task.SummaryMessage = ex.Message;
                RefreshRuntimeStatusText();
                ActiveSidePanel = InspectionWorkspaceSidePanelViewModel.CreateTask(task);
            });
        }
    }

    private void ApplyInspectionResult(
        InspectionTaskSessionViewModel task,
        InspectionCameraSessionViewModel camera,
        InspectionCycleResult result,
        bool includeAllTaskCameras = true)
    {
        task.ApplyCameraInspectionResult(
            camera,
            result,
            BuildInspectionSummary(result),
            includeAllTaskCameras);
        camera.ResultText = InspectionTaskSessionViewModel.FormatDecision(result.Decision);
        camera.StatusText = $"Last trigger: {result.TriggerTime:HH:mm:ss}";
        RefreshRuntimeStatusText();
        // 如果当前侧边栏已经是该任务（非概览），才自动刷新面板状态
        if (ActiveSidePanel is { IsOverview: false })
        {
            ActiveSidePanel = InspectionWorkspaceSidePanelViewModel.CreateTask(task);
        }
    }

    private static ProductPresenceInspectionResult AttachImageProductPresenceMetadata(
        ProductPresenceInspectionResult productPresence,
        string cameraId)
    {
        if (!productPresence.Enabled)
        {
            return productPresence;
        }

        var metadata = new Dictionary<string, string>(productPresence.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["presence.cameraId"] = cameraId,
            ["presence.primaryCameraId"] = cameraId,
            ["presence.skipped"] = productPresence.ShouldSkipInspection ? "true" : "false"
        };
        return productPresence with { Metadata = metadata };
    }

    private InspectionCycleResult CreateProductPresenceSkippedImageResult(
        InspectionTaskSessionViewModel task,
        InspectionCameraSessionViewModel camera,
        string productModel,
        string positionNo,
        DateTimeOffset triggerTime,
        ProductPresenceInspectionResult productPresence)
    {
        return new InspectionCycleResult
        {
            RecipeKey = new InspectionRecipeKey(productModel, task.RecipeTaskId, positionNo),
            StationId = task.StationId,
            TaskInstanceId = task.Id,
            CameraId = camera.Id,
            ActionType = task.ActionType,
            TriggerId = $"image-{triggerTime:yyyyMMddHHmmssfff}",
            TriggerTime = triggerTime,
            Operator = CreateCurrentOperatorSnapshot(),
            Decision = InspectionCycleDecision.Ng,
            SummaryMessage = productPresence.SummaryText,
            Calibration = CalibrationContext.Empty,
            ResolvedModels = string.IsNullOrWhiteSpace(productPresence.ModelId)
                ? Array.Empty<InspectionModelReference>()
                : new[]
                {
                    new InspectionModelReference
                    {
                        ModelId = productPresence.ModelId,
                        Alias = "product-presence",
                        Sequence = -1
                    }
                },
            ResolvedRois = Array.Empty<RoiDefinition>(),
            RoiResults = Array.Empty<InspectionRoiResult>(),
            Metadata = productPresence.Metadata
        };
    }

    private static InspectionCycleResult AttachProductPresenceResult(
        InspectionCycleResult result,
        ProductPresenceInspectionResult productPresence)
    {
        if (!productPresence.Enabled)
        {
            return result;
        }

        var metadata = new Dictionary<string, string>(result.Metadata, StringComparer.OrdinalIgnoreCase);
        foreach (var item in productPresence.Metadata)
        {
            metadata[item.Key] = item.Value;
        }

        var summary = string.IsNullOrWhiteSpace(result.SummaryMessage)
            ? productPresence.SummaryText
            : $"{productPresence.SummaryText}{Environment.NewLine}{result.SummaryMessage}";
        return result with
        {
            SummaryMessage = summary,
            Metadata = metadata
        };
    }

    public static string BuildInspectionSummary(InspectionCycleResult result)
    {
        var presenceLines = BuildProductPresenceSummaryLines(result).ToList();
        var roiResults = result.RoiResults.ToArray();
        if (roiResults.Length == 0)
        {
            return presenceLines.Count > 0
                ? string.Join(Environment.NewLine, presenceLines)
                : BuildEmptyResultSummary(result.Decision);
        }

        var lines = new List<string>();
        lines.AddRange(presenceLines);
        var includeRoiName = roiResults.Length > 1;
        foreach (var roi in roiResults)
        {
            var summary = BuildRoiInspectionSummary(roi);
            if (string.IsNullOrWhiteSpace(summary))
            {
                continue;
            }

            if (!includeRoiName)
            {
                lines.Add(summary.Trim());
                continue;
            }

            lines.Add($"{roi.RoiName}:");
            foreach (var line in SplitSummaryLines(summary))
            {
                lines.Add($"  {line}");
            }
        }

        return lines.Count == 0
            ? BuildEmptyResultSummary(result.Decision)
            : string.Join(Environment.NewLine, lines);
    }

    private static IEnumerable<string> BuildProductPresenceSummaryLines(InspectionCycleResult result)
    {
        if (!result.Metadata.TryGetValue("presence.enabled", out var enabled) ||
            !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(result.SummaryMessage))
        {
            foreach (var line in SplitSummaryLines(result.SummaryMessage)
                         .Where(line => line.StartsWith("产品有无", StringComparison.OrdinalIgnoreCase)))
            {
                yield return line;
            }

            yield break;
        }

        var decision = result.Metadata.TryGetValue("presence.decision", out var decisionText)
            ? decisionText
            : result.Metadata.TryGetValue("presence.status", out var status) &&
              string.Equals(status, "absent", StringComparison.OrdinalIgnoreCase)
                ? "无产品"
                : "有产品";
        var probability = result.Metadata.TryGetValue("presence.probability", out var probabilityText)
            ? probabilityText
            : string.Empty;
        var model = result.Metadata.TryGetValue("presence.modelId", out var modelId)
            ? modelId
            : string.Empty;

        var summaryLine = string.IsNullOrWhiteSpace(probability)
            ? $"产品有无：{decision}"
            : $"产品有无：{decision}，概率={probability}";
        if (!string.IsNullOrWhiteSpace(model))
        {
            summaryLine += $"，模型={model}";
        }

        if (result.Metadata.TryGetValue("presence.skipped", out var skipped) &&
            string.Equals(skipped, "true", StringComparison.OrdinalIgnoreCase))
        {
            summaryLine += "，已跳过划痕检测";
        }

        yield return summaryLine;
    }

    private static string BuildRoiInspectionSummary(InspectionRoiResult roi)
    {
        if (!string.IsNullOrWhiteSpace(roi.DefectComponentsText))
        {
            return FormatUnetComponentsText(roi.DefectComponentsText);
        }

        var isUnetResult = roi.DefectComponentCount.HasValue ||
                           !string.IsNullOrWhiteSpace(roi.DefectSummaryText);
        if (isUnetResult)
        {
            if (!string.IsNullOrWhiteSpace(roi.DefectSummaryText))
            {
                return roi.DefectSummaryText.Trim();
            }

            return "\u672a\u68c0\u51fa\u6709\u6548\u7f3a\u9677";
        }

        var findings = roi.Findings
            .Select(FormatFindingSummary)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
        if (findings.Length > 0)
        {
            return string.Join(Environment.NewLine, findings);
        }

        return roi.Decision switch
        {
            InspectionCycleDecision.Ok => "\u65e0\u7f3a\u9677",
            InspectionCycleDecision.Ng => "\u68c0\u6d4b NG",
            InspectionCycleDecision.Warning => "\u68c0\u6d4b WARNING",
            _ => "\u65e0\u68c0\u6d4b\u660e\u7ec6"
        };
    }

    private static string FormatFindingSummary(InspectionFinding finding)
    {
        if (!string.IsNullOrWhiteSpace(finding.Message))
        {
            return finding.Message.Trim();
        }

        return string.IsNullOrWhiteSpace(finding.Code) ? string.Empty : finding.Code.Trim();
    }

    private static string FormatUnetComponentsText(string text)
    {
        return string.Join(Environment.NewLine, SplitSummaryLines(text).Select(FormatUnetComponentLine));
    }

    private static string FormatUnetComponentLine(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var formatted = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (part.StartsWith("bbox=", StringComparison.OrdinalIgnoreCase) ||
                part.StartsWith("maxProb=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separatorIndex = part.IndexOf('=');
            if (separatorIndex <= 0)
            {
                formatted.Add(part);
                continue;
            }

            var key = part[..separatorIndex];
            var value = part[(separatorIndex + 1)..];
            var label = key switch
            {
                "area" => "\u9762\u79ef",
                "perimeter" => "\u5468\u957f",
                "ratio" => "\u9762\u79ef\u5468\u957f\u6bd4",
                "meanProb" => "\u6982\u7387",
                _ => key
            };
            formatted.Add($"{label}={value}");
        }

        return string.Join(" ", formatted);
    }

    private static IEnumerable<string> SplitSummaryLines(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string BuildEmptyResultSummary(InspectionCycleDecision decision)
    {
        return decision switch
        {
            InspectionCycleDecision.Ok => "\u65e0\u7f3a\u9677",
            InspectionCycleDecision.Ng => "\u68c0\u6d4b NG",
            InspectionCycleDecision.Warning => "\u68c0\u6d4b WARNING",
            _ => "\u65e0\u68c0\u6d4b\u660e\u7ec6"
        };
    }

    private static void ApplyResultOverlays(InspectionRuntimeFrameResult frame)
    {
        frame.Camera.ResultOverlayItems.Clear();
        var roiResults = frame.Result.RoiResults.ToDictionary(item => item.RoiId, StringComparer.OrdinalIgnoreCase);
        foreach (var roi in frame.Result.ResolvedRois.OrderBy(item => item.SortOrder))
        {
            var decision = roiResults.TryGetValue(roi.Id, out var roiResult)
                ? roiResult.Decision
                : frame.Result.Decision;
            var stroke = GetDecisionBrush(decision);
            var width = roi.Width * frame.ImageWidth;
            var height = roi.Height * frame.ImageHeight;
            var left = (roi.CenterX * frame.ImageWidth) - (width / 2);
            var top = (roi.CenterY * frame.ImageHeight) - (height / 2);
            frame.Camera.ResultOverlayItems.Add(new ImageOverlayItem
            {
                Kind = ImageOverlayKind.RotatedRectangle,
                X = left,
                Y = top,
                Width = width,
                Height = height,
                Angle = roi.AngleDeg,
                Stroke = stroke,
                Fill = null,
                StrokeThickness = decision == InspectionCycleDecision.Ng ? 4 : 3
            });
        }
    }

    private static Brush GetDecisionBrush(InspectionCycleDecision decision)
    {
        var color = decision switch
        {
            InspectionCycleDecision.Ok => Color.FromRgb(28, 160, 84),
            InspectionCycleDecision.Ng => Color.FromRgb(220, 38, 38),
            InspectionCycleDecision.Warning => Color.FromRgb(245, 158, 11),
            _ => Color.FromRgb(100, 116, 139)
        };
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void LoadImageForActiveCamera(string? path)
    {
        if (ActiveCameraSession != null)
        {
            LoadImage(ActiveCameraSession, path);
        }
    }

    private static void LoadImage(InspectionCameraSessionViewModel camera, string? path)
    {
        camera.ResultOverlayItems.Clear();
        var imagePath = path?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            camera.FrameImage = null;
            camera.FrameImagePath = string.Empty;
            camera.StatusText = string.IsNullOrWhiteSpace(imagePath)
                ? (camera.IsRunning ? "\u7b49\u5f85\u56fe\u50cf" : InspectionCameraSessionViewModel.IdleStatusText)
                : $"\u56fe\u50cf\u4e0d\u5b58\u5728: {imagePath}";
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            camera.FrameImage = bitmap;
            camera.FrameImagePath = imagePath;
            camera.StatusText = $"\u5f53\u524d\u56fe\u50cf: {Path.GetFileName(imagePath)}";
        }
        catch
        {
            camera.FrameImage = null;
            camera.FrameImagePath = string.Empty;
            camera.StatusText = $"\u65e0\u6cd5\u52a0\u8f7d\u56fe\u50cf: {imagePath}";
        }
    }

    private static RoiListItemViewModel CreateRoiItem(InspectionRoiConfig roi)
    {
        return new RoiListItemViewModel
        {
            Name = roi.Name,
            CenterX = roi.CenterX.ToString("0.###"),
            CenterY = roi.CenterY.ToString("0.###"),
            Width = roi.Width.ToString("0.###"),
            Height = roi.Height.ToString("0.###"),
            AngleDeg = roi.AngleDeg.ToString("0.#"),
            ModelName = roi.ModelId,
            Summary = $"Center ({roi.CenterX:0.###}, {roi.CenterY:0.###}) / Size ({roi.Width:0.###} x {roi.Height:0.###}) / Angle {roi.AngleDeg:0.#}",
            ModelSummary = string.IsNullOrWhiteSpace(roi.ModelId) ? "Model: Unassigned" : $"Model: {roi.ModelId}",
            StatusText = roi.Enabled ? "Enabled" : "Disabled",
            LeftPx = (roi.CenterX * 920) - (roi.Width * 920 / 2),
            TopPx = (roi.CenterY * 540) - (roi.Height * 540 / 2),
            WidthPx = roi.Width * 920,
            HeightPx = roi.Height * 540,
            StrokeBrush = roi.Enabled ? Brushes.Orange : Brushes.SlateGray,
            FillBrush = roi.Enabled
                ? new SolidColorBrush(Color.FromArgb(40, 242, 140, 40))
                : new SolidColorBrush(Color.FromArgb(28, 106, 122, 138))
        };
    }

    private void ApplyRuntimeRecipeContext()
    {
        if (TaskSessions.Count == 0)
        {
            return;
        }

        foreach (var task in TaskSessions)
        {
            ReloadRecipeContext(task);
        }

        ActiveSidePanel = SelectedWorkspaceTab?.IsOverview == true
            ? InspectionWorkspaceSidePanelViewModel.CreateOverview(TaskSessions, CameraSessions)
            : InspectionWorkspaceSidePanelViewModel.CreateTask(ActiveTaskSession);
    }

    private void RefreshRuntimeStatusText()
    {
        if (TaskSessions.Count == 0)
        {
            RuntimeStatusText = "无任务";
            return;
        }

        var startingCount = TaskSessions.Count(task => string.Equals(task.StatusText, "启动中", StringComparison.OrdinalIgnoreCase));
        if (startingCount > 0)
        {
            RuntimeStatusText = startingCount == TaskSessions.Count
                ? "全部任务启动中"
                : $"{startingCount}/{TaskSessions.Count} 个任务启动中";
            return;
        }

        var stoppingCount = TaskSessions.Count(task => string.Equals(task.StatusText, "停止中", StringComparison.OrdinalIgnoreCase));
        if (stoppingCount > 0)
        {
            RuntimeStatusText = stoppingCount == TaskSessions.Count
                ? "全部任务停止中"
                : $"{stoppingCount}/{TaskSessions.Count} 个任务停止中";
            return;
        }

        var failedCount = TaskSessions.Count(task =>
            string.Equals(task.StatusText, "启动失败", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(task.StatusText, "运行异常", StringComparison.OrdinalIgnoreCase));
        if (failedCount > 0)
        {
            RuntimeStatusText = $"{failedCount}/{TaskSessions.Count} 个任务异常";
            return;
        }

        var runningCount = TaskSessions.Count(task => task.IsRunning);
        RuntimeStatusText = runningCount switch
        {
            0 => "全部停止",
            var count when count == TaskSessions.Count => $"全部运行中（{count} 个任务）",
            _ => $"{runningCount}/{TaskSessions.Count} 个任务运行中"
        };
    }

    private static string NormalizeRecipePart(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private bool EnsureLoggedIn()
    {
        if (_authenticationService.IsLoggedIn)
        {
            return true;
        }

        RuntimeStatusText = "未登录";
        if (ActiveTaskSession != null)
        {
            ActiveTaskSession.SummaryMessage = "请先登录生产员工。";
            ActiveSidePanel = InspectionWorkspaceSidePanelViewModel.CreateTask(ActiveTaskSession);
        }

        return false;
    }

    private InspectionOperatorSnapshot? CreateCurrentOperatorSnapshot()
    {
        var session = _authenticationService.CurrentSession;
        return session == null ? null : InspectionOperatorSnapshot.FromSession(session);
    }

    private void OnAuthenticationSessionChanged(object? sender, EventArgs e)
    {
        NotifyLoginPresentation();
    }

    private void NotifyLoginPresentation()
    {
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(CurrentLoginDisplayText));
    }

    public void Dispose()
    {
        _authenticationService.CurrentSessionChanged -= OnAuthenticationSessionChanged;
        _resultStorageService.StatusChanged -= OnResultStorageStatusChanged;
        _plcTriggerController.StatusChanged -= OnPlcTriggerStatusChanged;
        _plcTriggerController.Dispose();
        _runtime.FrameProcessed -= OnRuntimeFrameProcessed;
        _runtime.TaskFailed -= OnRuntimeTaskFailed;
        _runtime.Dispose();
        _resultStorageService.Dispose();
        _retentionCleanupService?.Dispose();
    }
}
