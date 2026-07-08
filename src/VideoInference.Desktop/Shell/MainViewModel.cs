using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewLife.Log;

namespace VideoInferenceDemo;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private const string DefaultWindowTitle = "SOP \u5408\u89c4\u68c0\u6d4b\u53f0";
    private const string DefaultHomeTitle = "SOP \u5408\u89c4\u68c0\u6d4b\u53f0";
    private readonly CameraProviderRegistry _cameraProviders;
    private readonly SqliteResultWriter _resultWriter;
    private readonly SqliteAnalysisResultWriter _analysisResultWriter;
    private readonly TcnLabelWriter _labelWriter;
    private readonly TcnFeatureWriter? _featureWriter;
    private readonly TcnOnnxInferenceEngine? _tcnEngine;
    private readonly TcnPredictionRecorder? _tcnPredictionRecorder;
    private readonly AnalysisEngine? _analysisEngine;
    private readonly VisionWorkspaceService _visionWorkspace;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ObservableCollection<FsmStepItem> _videoFsmSteps = new();
    private readonly List<FsmStepSnapshot> _videoFsmStepSnapshots = new();
    private readonly IDesktopDialogService _dialogService;
    private readonly IDesktopPipelineSupportFactory _pipelineSupportFactory;
    private readonly IUiTimerFactory _uiTimerFactory;
    private readonly IEnvironmentDiagnosticsService _environmentDiagnosticsService;
    private readonly IDesktopNativeRuntimeService _nativeRuntimeService;
    private readonly IUiTimer _sopTimelineTimer;
    private readonly ModelActivationService _modelActivationService;
    private readonly VisionTaskFactoryRegistry _visionTaskFactoryRegistry;
    private readonly PersonnelRepository _personnelRepository;
    private readonly SopAlarmEventRepository _sopAlarmEventRepository;
    private readonly FingerprintRecognitionHost _fingerprintRecognitionHost;
    private readonly ModbusIOController _modbusIOController;
    private readonly ModbusHoldingRegisterBank _modbusRegisterBank;
    private readonly ModbusTcpServerHost _modbusTcpServerHost;
    private readonly ProductionDashboardQueryService _productionDashboardQueryService;
    private readonly SessionTaskOrchestrator _sessionTaskOrchestrator;
    private readonly WorkspaceRunCoordinator _workspaceRunCoordinator;
    private readonly CameraSessionWorkspaceCoordinator _cameraSessionWorkspaceCoordinator;
    private readonly CameraSessionLifecycleCoordinator _cameraSessionLifecycleCoordinator;
    private readonly WorkspaceSelectionCoordinator _workspaceSelectionCoordinator;
    private readonly WorkspaceProjectionBuilder _workspaceProjectionBuilder;
    private readonly CameraSettingsRepository _cameraSettingsRepository;
    private readonly HardwareSettingsRepository _hardwareSettingsRepository;
    private readonly PersonnelAuthenticationService _authenticationService;
    private readonly WorkspaceDatabasePaths _databasePaths;
    private RetentionCleanupService? _retentionCleanupService;
    private readonly Dictionary<string, string> _activeSopAlarmEventUuids = new(StringComparer.OrdinalIgnoreCase);
    private ModelActivationStatusInfo _modelActivationStatus = new(ModelActivationState.NoModel, "无模型", "DL 目录下未发现可用模型。", "-");

    public MainViewModel(
        CameraProviderRegistry? cameraProviders = null,
        IDesktopDialogService? dialogService = null,
        IUiTimerFactory? uiTimerFactory = null,
        IUiDispatcher? uiDispatcher = null,
        IDesktopPipelineSupportFactory? pipelineSupportFactory = null,
        IEnvironmentDiagnosticsService? environmentDiagnosticsService = null,
        IDesktopNativeRuntimeService? nativeRuntimeService = null,
        PersonnelAuthenticationService? authenticationService = null,
        WorkspaceDatabasePaths? databasePaths = null)
    {
        var branding = AppConfigStorage.LoadBranding(
            AppContext.BaseDirectory,
            AppBrandingKeys.VideoInference,
            DefaultWindowTitle,
            DefaultHomeTitle);
        WindowTitle = branding.WindowTitle;
        HomeTitle = branding.HomeTitle;

        _cameraProviders = cameraProviders ?? DesktopCameraProviderRegistry.CreateDefault();
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _uiTimerFactory = uiTimerFactory ?? throw new ArgumentNullException(nameof(uiTimerFactory));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _pipelineSupportFactory = pipelineSupportFactory ?? throw new ArgumentNullException(nameof(pipelineSupportFactory));
        _environmentDiagnosticsService = environmentDiagnosticsService ?? new DesktopEnvironmentDiagnosticsService();
        _nativeRuntimeService = nativeRuntimeService ?? new DesktopNativeRuntimeService();
        _sopTimelineTimer = _uiTimerFactory.CreatePeriodic(TimeSpan.FromMilliseconds(33), RefreshSopTimelineDisplays);
        _modelActivationService = new ModelActivationService();
        _visionWorkspace = new VisionWorkspaceService(AppContext.BaseDirectory, _modelActivationService);
        _visionTaskFactoryRegistry = new VisionTaskFactoryRegistry(new IVisionTaskFactory[]
        {
            OnnxVisionTaskFactory.Instance,
            MediaPipeHandTaskFactory.Instance,
            OcrVisionTaskFactory.Instance
        });
        TargetFpsText = "10";
        StatusText = "Idle";
        SourceLabel = "None";
        InferenceStatus = "Waiting for input";
        LastFrameInfo = "-";
        FsmSteps = _videoFsmSteps;
        _deviceKind = InferenceDeviceKind.GpuCuda;

        _databasePaths = databasePaths ?? (DbSession.IsInitialized
            ? WorkspaceDatabaseBootstrap.ResolvePaths(AppContext.BaseDirectory)
            : WorkspaceDatabaseBootstrap.Initialize(AppContext.BaseDirectory));
        var configDbPath = _databasePaths.ConfigDbPath;
        var resultsDirectory = _databasePaths.ResultsDirectory;
        _cameraSettingsRepository = new CameraSettingsRepository(configDbPath);
        _hardwareSettingsRepository = new HardwareSettingsRepository(configDbPath);

        ImportLegacyConfigsToSqlite(configDbPath);

        _dbConfig = _hardwareSettingsRepository.LoadDbConfig();
        _analysisConfig = _hardwareSettingsRepository.LoadAnalysisConfig();
        RestartRetentionCleanupService(resultsDirectory);
        _resultWriter = new SqliteResultWriter(enableRawDetections: _dbConfig.EnableRawDetections);
        _analysisResultWriter = new SqliteAnalysisResultWriter();
        _personnelRepository = new PersonnelRepository(configDbPath);
        _personnelRepository.EnsureDefaultAdmin();
        _authenticationService = authenticationService ?? new PersonnelAuthenticationService(_personnelRepository);
        _authenticationService.CurrentSessionChanged += OnAuthenticationSessionChanged;
        NormalizePersonnelFingerprintBindings();
        _sopAlarmEventRepository = new SopAlarmEventRepository(resultsDirectory);
        _fingerprintRecognitionHost = new FingerprintRecognitionHost(_personnelRepository);
        _fingerprintRecognitionHost.Recognized += OnFingerprintRecognized;
        _fingerprintRecognitionHost.Error += OnFingerprintError;
        _modbusIOController = new ModbusIOController();
        _modbusRegisterBank = new ModbusHoldingRegisterBank();
        _modbusTcpServerHost = new ModbusTcpServerHost(_modbusRegisterBank);
        _productionDashboardQueryService = new ProductionDashboardQueryService(resultsDirectory);
        _labelWriter = new TcnLabelWriter();
        _featureWriter = _dbConfig.EnableTcnFeatures ? new TcnFeatureWriter() : null;
        _sessionTaskOrchestrator = new SessionTaskOrchestrator(
            _cameraProviders,
            _resultWriter,
            _analysisResultWriter,
            _labelWriter,
            _featureWriter,
            _uiDispatcher,
            _pipelineSupportFactory,
            _visionTaskFactoryRegistry,
            _modbusRegisterBank);
        _workspaceRunCoordinator = new WorkspaceRunCoordinator(
            _personnelRepository,
            new RunOperatorAssignmentRepository(resultsDirectory),
            new RunProductionStatsRepository(resultsDirectory));
        _cameraSessionWorkspaceCoordinator = new CameraSessionWorkspaceCoordinator(
            _sessionTaskOrchestrator,
            _workspaceRunCoordinator);
        _cameraSessionLifecycleCoordinator = new CameraSessionLifecycleCoordinator(_workspaceRunCoordinator);
        _workspaceSelectionCoordinator = new WorkspaceSelectionCoordinator(_sessionTaskOrchestrator);
        _workspaceProjectionBuilder = new WorkspaceProjectionBuilder();
        _tcnEngine = _dbConfig.EnableTcnInference ? _pipelineSupportFactory.TryCreateTcnEngine() : null;
        _tcnPredictionRecorder = _tcnEngine != null ? new TcnPredictionRecorder(_tcnEngine, _labelWriter) : null;
        _analysisEngine = _analysisConfig.EnableOnlineAnalysis
            ? new AnalysisEngine(_analysisConfig, modbusRegisters: _modbusRegisterBank)
            : null;
        if (_analysisEngine != null)
        {
            _analysisEngine.ResultReady += OnAnalysisResult;
        }

        // Legacy JSON config migration
        _cameraSettingsRepository.ImportFromFileIfEmpty(CameraConfigPath);

        _resultWriter.Start();
        _analysisResultWriter.Start();
        _labelWriter.Start();
        _featureWriter?.Start();
        _tcnEngine?.Start();

        _nativeRuntimeService.Initialize();
        LoadModelCatalog();
        MaterializeWorkspaceSelection(WorkspaceSelectionMode.Auto);
        ReloadAppConfigurationState();
        LoadPersonnelOptions();
        LoadCameraConfig();
        RestartFingerprintRecognitionHost();
        _sopTimelineTimer.Start();
        XTrace.WriteLine("[MainViewModel] Initialization complete. cameras={0}, tasks={1}, sopProfiles={2}",
            CameraSessions.Count, AvailableVisionTasks.Count, _sopProfiles.Count);
    }

    [ObservableProperty]
    private ImageSource? videoFrame;

    public string WindowTitle { get; }

    public string HomeTitle { get; }

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private string targetFpsText = string.Empty;

    [ObservableProperty]
    private string sourceLabel = string.Empty;

    [ObservableProperty]
    private string inferenceStatus = string.Empty;

    [ObservableProperty]
    private string inferenceDeviceText = "-";

    [ObservableProperty]
    private string lastFrameInfo = string.Empty;

    [ObservableProperty]
    private string lastError = string.Empty;

    public bool HasError => !string.IsNullOrWhiteSpace(LastError);
    private bool IsSopAlarmActive => CanResetSopFault || CameraSessions.Any(session => session.CanResetSopFault);

    [ObservableProperty]
    private bool? lastTransitionOk;

    [ObservableProperty]
    private double captureFps;

    [ObservableProperty]
    private double inferFps;

    [ObservableProperty]
    private double renderFps;

    [ObservableProperty]
    private string sourceFpsText = "-";

    [ObservableProperty]
    private string sourceDurationText = "-";

    [ObservableProperty]
    private string playbackTimeText = "-";

    [ObservableProperty]
    private string currentTimeText = "-";

    [ObservableProperty]
    private bool isVideoSource;

    [ObservableProperty]
    private int frameQueueSize;

    [ObservableProperty]
    private int renderQueueSize;

    [ObservableProperty]
    private long droppedByPts;

    [ObservableProperty]
    private long droppedByCaptureQueue;

    [ObservableProperty]
    private long droppedByInferDrain;

    [ObservableProperty]
    private long droppedByRenderQueue;

    [ObservableProperty]
    private long droppedByRenderDrain;

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private bool isPaused;

    [ObservableProperty]
    private bool isStatsExpanded;

    [ObservableProperty]
    private bool isSopExpanded = true;

    [ObservableProperty]
    private bool isMetricsExpanded = true;

    [ObservableProperty]
    private ObservableCollection<FsmStepItem> fsmSteps = new();

    [ObservableProperty]
    private string sopOutcomeText = "等待";

    [ObservableProperty]
    private string sopOutcomeBackground = "#EDF2F7";

    [ObservableProperty]
    private string sopOutcomeForeground = "#607080";

    [ObservableProperty]
    private bool canResetSopFault;

    [ObservableProperty]
    private ModelCatalogEntry? selectedModel;

    [ObservableProperty]
    private VisionTaskDefinition? selectedPrimaryTask;

    [ObservableProperty]
    private CameraSessionViewModel? selectedCameraSession;

    [ObservableProperty]
    private PersonnelOptionItem? selectedPersonnel;

    [ObservableProperty]
    private string modelCatalogStatusText = "DL 目录下未发现模型。";
    public ObservableCollection<ModelCatalogEntry> AvailableModels { get; } = new();
    public ObservableCollection<VisionTaskDefinition> AvailableVisionTasks { get; } = new();
    public ObservableCollection<CameraSessionViewModel> CameraSessions { get; } = new();
    public ObservableCollection<PersonnelOptionItem> PersonnelOptions { get; } = new();

    private PrimaryVisionTaskBinding? _activePrimaryBinding;
    private InferenceDeviceKind _deviceKind;
    private float _confidenceThreshold = 0.25f;
    private float _nmsThreshold = 0.45f;
    private SessionRunState _topLevelRunState = SessionRunState.Idle;

    private static readonly string CameraConfigPath = Path.Combine(AppContext.BaseDirectory, "camera_config.json");
    private DbConfig _dbConfig = new();
    private AnalysisConfig _analysisConfig = new();
    private CameraSettings _cameraSettings = CameraSettings.CreateDefault();
    private IReadOnlyList<SopProfile> _sopProfiles = new List<SopProfile>();

    public SessionStatusSnapshot StatusSnapshot => BuildStatusSnapshot();
    public SessionStatusSnapshot WorkspaceStatusSnapshot => CurrentSession?.StatusSnapshot ?? StatusSnapshot;
    public string ControlSourceText => IsVideoSource
        ? "文件回放"
        : SelectedCameraSession != null
            ? $"实时采集 / {SelectedCameraSession.Name}"
            : "实时采集";
    public string RunStateText => GetRunStateText(StatusSnapshot);
    public string ControlHintText => IsVideoSource
        ? (IsPaused
            ? "继续会从当前位置恢复；停止后点击播放会从头重新播放当前视频。"
            : "视频文件支持播放、暂停和停止，框绘制会跟随当前显示帧。")
        : "相机模式支持按 Tab 切换画面，并分别启动、停止每一路相机。";
    public string PlayButtonText => IsPaused ? "继续" : "播放";
    public string TargetFpsDisplay => double.TryParse(TargetFpsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps) && fps > 0
        ? fps.ToString("F0", CultureInfo.InvariantCulture)
        : "-";
    public string TransitionStatusText => GetTransitionStatusText(StatusSnapshot);
    public string StatusBadgeText => GetStatusBadgeText(StatusSnapshot);
    public string SelectedPrimaryTaskText => SelectedPrimaryTask?.DisplayName ?? "-";
    public string SelectedPrimaryTaskKindText => SelectedPrimaryTask?.TaskKind.ToString() ?? "-";
    public string SelectedPrimaryTaskRuntimeText => SelectedPrimaryTask?.RuntimeKind.ToString() ?? "-";
    public bool HasSelectedPrimaryTask => SelectedPrimaryTask != null;
    public string PrimaryTaskModelSourceText => SelectedModel?.DisplayName ?? "无模型来源";
    public ModelWorkspaceStatusInfo ModelWorkspaceStatus => _visionWorkspace.CurrentSnapshot.WorkspaceStatusInfo;
    public ModelActivationStatusInfo ModelActivationStatus => _modelActivationStatus;
    public string PrimaryTaskModelSourcePathText => SelectedModel?.RelativeModelPath ?? "-";
    public string PrimaryTaskModelSourceDescriptionText => !string.IsNullOrWhiteSpace(SelectedModel?.Description)
        ? SelectedModel.Description
        : "当前主任务未绑定模型目录来源。";
    public bool IsLoggedIn => _authenticationService.IsLoggedIn;
    public string CurrentLoginDisplayText => _authenticationService.CurrentSession?.DisplayText ?? "未登录";
    public string SelectedPersonnelCode => SelectedPersonnel?.EmployeeCode ?? string.Empty;
    public string SelectedPersonnelDisplayText => _authenticationService.CurrentSession?.DisplayText ?? "未登录";
    public string CurrentSessionPersonnelDisplayText => (SelectedCameraSession != null &&
                                                         _workspaceRunCoordinator.TryGetActiveBinding(SelectedCameraSession.Id, out var binding))
        ? binding.EmployeeName
        : SelectedPersonnelDisplayText;
    public bool HasCameraSessions => CameraSessions.Count > 0;
    public bool IsSystemStarting => CameraSessions.Any(s => s.RunState == SessionRunState.Starting);
    public bool IsSystemRunning => !IsSystemStarting && CameraSessions.Any(s => s.IsRunning);
    public bool IsSystemIdle => !IsSystemStarting && !IsSystemRunning;
    public string SystemStatusText => IsSystemStarting ? "启动中" : IsSystemRunning ? "运行中" : "停止中";
    public bool IsVideoPreviewVisible => IsVideoSource || !HasCameraSessions;
    public bool IsCameraPreviewVisible => !IsVideoSource && HasCameraSessions;
    public bool IsCameraSessionStripVisible => IsVideoSource && HasCameraSessions;

    private CameraSessionViewModel? CurrentSession => SelectedCameraSession;
    public WorkspaceProjectionSnapshot Workspace => BuildWorkspaceProjection();

    private bool MaterializeWorkspaceSelection(WorkspaceSelectionMode mode)
    {
        if (IsWorkspaceSelectionAlreadyMaterialized())
        {
            return true;
        }

        var result = _workspaceSelectionCoordinator.MaterializeSelection(
            _visionWorkspace,
            CameraSessions,
            BuildVisionTaskCreationContext(),
            mode,
            IsVideoSource);
        ApplyWorkspaceSelectionMaterializationResult(result);
        return result.Success;
    }

    private bool IsWorkspaceSelectionAlreadyMaterialized()
    {
        var selectedTask = _visionWorkspace.CurrentSnapshot.SelectedPrimaryTask;
        if (selectedTask == null || _activePrimaryBinding == null)
        {
            return false;
        }

        if (!string.Equals(_activePrimaryBinding.Definition.Id, selectedTask.Id, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_activePrimaryBinding.Context != BuildVisionTaskCreationContext())
        {
            return false;
        }

        var enabledSessions = CameraSessions.Where(session => session.Profile.Enabled).ToArray();
        if (enabledSessions.Length == 0)
        {
            return false;
        }

        return enabledSessions.All(session =>
            string.Equals(session.ActivePrimaryTaskId, selectedTask.Id, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyWorkspaceSelectionMaterializationResult(WorkspaceSelectionMaterializationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        ApplyWorkspaceCatalogSnapshot(
            result.WorkspaceSnapshot,
            updateSelectedPrimaryTaskModelSource: true,
            updateSelectedTask: true,
            refreshCatalogCollections: false);
        _activePrimaryBinding = result.ActivePrimaryBinding;

        if (result.ModelBackedActivationAttempt != null)
        {
            _modelActivationStatus = ModelActivationStatusInfo.FromAttempt(result.ModelBackedActivationAttempt);
        }
        else if (result.Success && result.WorkspaceSnapshot.SelectedPrimaryTask != null)
        {
            _modelActivationStatus = new ModelActivationStatusInfo(
                ModelActivationState.NoModel,
                "任务模式",
                $"{result.WorkspaceSnapshot.SelectedPrimaryTask.DisplayName} 已作为主任务装载。",
                result.InferenceDeviceText);
        }

        OnPropertyChanged(nameof(ModelActivationStatus));
        SetTopLevelRunState(ResolveTopLevelRunState(result));
        LastError = result.LastError ?? string.Empty;
        StatusText = result.StatusText;
        InferenceStatus = result.InferenceStatus;
        InferenceDeviceText = result.InferenceDeviceText;

        if (SelectedCameraSession != null)
        {
            ApplySelectedCameraWorkspaceState();
        }

        NotifyWorkspaceProjectionChanged();
    }

    [RelayCommand]
    private async Task OpenVideo()
    {
        if (!EnsurePersonnelSelected())
        {
            return;
        }

        var path = _dialogService.OpenVideoFile();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var session = _cameraSessionWorkspaceCoordinator.ResolvePreferredInteractiveSession(
            _cameraSettings,
            SelectedCameraSession,
            CameraSessions);
        var precheck = session != null
            ? FastCheckTaskExists(session)
            : new SessionStartPrecheckResult(SessionStartPrecheckState.NoModel, "No session is available for video playback.");
        if (session == null || !precheck.IsSuccess)
        {
            ApplyCameraSessionStartPreparation(new CameraSessionStartPreparationResult(null, precheck));
            return;
        }

        // Load model on background thread to avoid UI freeze
        var binding = GetModelBinding(session);
        if (binding != null)
        {
            try
            {
                var loadedTask = await Task.Run(() => binding.Registry.Create(binding.Definition, binding.Context));
                session.ApplyPrimaryVisionTask(binding, loadedTask);
                _activePrimaryBinding = binding;
            }
            catch (Exception ex)
            {
                LastError = $"Failed to load model: {ex.Message}";
                return;
            }
        }

        var launchResult = _cameraSessionWorkspaceCoordinator.OpenVideoOnPreferredSession(
            _cameraSettings,
            SelectedCameraSession,
            CameraSessions,
            path,
            ParseTargetFps(),
            GetPreferredVideoPtsMode());
        if (!ApplyInteractiveSessionLaunchResult(launchResult, refreshControlCommands: true))
        {
            return;
        }
    }

    [RelayCommand(CanExecute = nameof(CanPlayVideo))]
    private void PlayVideo()
    {
        var session = SelectedCameraSession;
        if (session == null)
        {
            return;
        }

        if (!session.IsPaused && !EnsurePersonnelSelected())
        {
            return;
        }

        if (!session.PlayVideo(ParseTargetFps(), GetPreferredVideoPtsMode()))
        {
            ApplySelectedCameraWorkspaceState();
            return;
        }

        ApplySelectedCameraWorkspaceState();
    }

    private bool CanPlayVideo()
    {
        var session = SelectedCameraSession;
        return session?.ControlState.CanPlayVideo == true;
    }

    [RelayCommand(CanExecute = nameof(CanPausePlayback))]
    private void PausePlayback()
    {
        var session = SelectedCameraSession;
        if (session == null)
        {
            return;
        }

        session.PauseVideo();
        ApplySelectedCameraWorkspaceState();
    }

    private bool CanPausePlayback() => SelectedCameraSession?.ControlState.CanPauseVideo == true;

    [RelayCommand]
    private void StartCamera()
    {
        if (!EnsurePersonnelSelected())
        {
            return;
        }

        ApplyInteractiveSessionLaunchResult(
            _cameraSessionWorkspaceCoordinator.StartPreferredInteractiveCamera(
                _cameraSettings,
                SelectedCameraSession,
                CameraSessions),
            refreshControlCommands: false);
    }

    [RelayCommand(CanExecute = nameof(CanStartAllCameras))]
    private void StartAllCameras()
    {
        if (!EnsurePersonnelSelected())
        {
            return;
        }

        StartConfiguredCameraProfiles(_cameraSettings.GetEnabledCameras());
    }

    [RelayCommand(CanExecute = nameof(CanStopAllCameras))]
    private void StopAllCameras()
    {
        StopAllCameraSessions();
        if (!Workspace.IsVideoSource)
        {
            ApplySelectedCameraWorkspaceState();
        }
    }

    private bool CanStartAllCameras() => HasCameraSessions && CameraSessions.Any(s => !s.IsRunning);
    private bool CanStopAllCameras() => HasCameraSessions && CameraSessions.Any(s => s.IsRunning);

    [RelayCommand]
    private void Login()
    {
        if (!_dialogService.ShowLogin(_authenticationService))
        {
            return;
        }

        LoadPersonnelOptions();
        RebindRunningSessionsToCurrentPersonnel();
        StatusText = $"已登录：{CurrentLoginDisplayText}";
        InferenceStatus = StatusText;
        RefreshPersonnelPresentation();
    }

    [RelayCommand]
    private void Logout()
    {
        StopAllCameraSessions();
        _authenticationService.Logout();
        SelectedPersonnel = null;
        StatusText = "已注销";
        InferenceStatus = "请登录后再启动生产。";
        RefreshPersonnelPresentation();
        RefreshWorkspacePresentation();
    }

    [RelayCommand(CanExecute = nameof(CanStopPipeline))]
    private void Stop()
    {
        SelectedCameraSession?.Stop();
        ApplySelectedCameraWorkspaceState();
    }

    private bool CanStopPipeline() => SelectedCameraSession?.IsRunning == true;

    [RelayCommand]
    private void ApplyFps()
    {
        if (!Workspace.IsVideoSource)
        {
            InferenceStatus = "Per-camera FPS is configured in Camera Settings.";
            return;
        }

        var fps = ParseTargetFps();
        SelectedCameraSession?.UpdatePlaybackTargetFps(fps);
        InferenceStatus = $"Target FPS set to {fps:F0}";
    }


    public void Dispose()
    {
        _sopTimelineTimer.Dispose();
        _authenticationService.CurrentSessionChanged -= OnAuthenticationSessionChanged;
        _fingerprintRecognitionHost.Recognized -= OnFingerprintRecognized;
        _fingerprintRecognitionHost.Error -= OnFingerprintError;
        _fingerprintRecognitionHost.Dispose();
        _modbusIOController.Dispose();
        _modbusTcpServerHost.Dispose();
        StopAllCameraSessions();
        _tcnPredictionRecorder?.Flush();
        _tcnPredictionRecorder?.Dispose();
        if (_analysisEngine != null)
        {
            _analysisEngine.ResultReady -= OnAnalysisResult;
        }
        _resultWriter.Dispose();
        _analysisResultWriter.Dispose();
        _labelWriter.Dispose();
        _featureWriter?.Dispose();
        _tcnEngine?.Dispose();
        _retentionCleanupService?.Dispose();
    }

    private void NotifyWorkspaceProjectionChanged()
    {
        OnPropertyChanged(nameof(Workspace));
    }

    private void RefreshWorkspacePresentation()
    {
        NotifyWorkspaceProjectionChanged();
    }

    private void RefreshStatusPresentation()
    {
        NotifyStatusSnapshotChanged();
        RefreshWorkspacePresentation();
    }

    private void RefreshPersonnelPresentation()
    {
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(CurrentLoginDisplayText));
        OnPropertyChanged(nameof(SelectedPersonnelCode));
        OnPropertyChanged(nameof(SelectedPersonnelDisplayText));
        OnPropertyChanged(nameof(CurrentSessionPersonnelDisplayText));
        RefreshWorkspacePresentation();
    }

    private void OnAuthenticationSessionChanged(object? sender, EventArgs e)
    {
        LoadPersonnelOptions();
        RefreshPersonnelPresentation();
    }

    private void RebindRunningSessionsToCurrentPersonnel()
    {
        if (SelectedPersonnel == null)
        {
            return;
        }

        foreach (var session in CameraSessions.Where(session => session.IsRunning))
        {
            _workspaceRunCoordinator.TryBindSessionRun(session, SelectedPersonnel);
        }

        OnPropertyChanged(nameof(CurrentSessionPersonnelDisplayText));
    }

    private void RefreshSelectedSessionPresentation()
    {
        NotifyStatusSnapshotChanged();
        OnPropertyChanged(nameof(ControlSourceText));
        OnPropertyChanged(nameof(CurrentSessionPersonnelDisplayText));
        NotifyControlCommandsChanged();
        ApplySelectedCameraWorkspaceState();
    }

    private void RefreshCameraSessionCollectionPresentation()
    {
        OnPropertyChanged(nameof(HasCameraSessions));
        OnPropertyChanged(nameof(IsVideoPreviewVisible));
        OnPropertyChanged(nameof(IsCameraPreviewVisible));
        OnPropertyChanged(nameof(IsCameraSessionStripVisible));
        RefreshWorkspacePresentation();
        NotifyModelCommandsChanged();
        StartAllCamerasCommand.NotifyCanExecuteChanged();
        StopAllCamerasCommand.NotifyCanExecuteChanged();
        NotifySystemStatusChanged();
    }

    private void RefreshRunControlPresentation(bool includePlayButtonText, bool refreshModelCommands)
    {
        OnPropertyChanged(nameof(RunStateText));
        OnPropertyChanged(nameof(ControlHintText));
        if (includePlayButtonText)
        {
            OnPropertyChanged(nameof(PlayButtonText));
        }

        OnPropertyChanged(nameof(StatusBadgeText));
        RefreshWorkspacePresentation();
        NotifyControlCommandsChanged();
        if (refreshModelCommands)
        {
            NotifyModelCommandsChanged();
        }
    }

    public string SourceFpsDisplay => SourceFpsText;
    public string SourceDurationDisplay => SourceDurationText;
    public string PlaybackTimeDisplay => PlaybackTimeText;

    partial void OnIsVideoSourceChanged(bool value)
    {
        NotifyStatusSnapshotChanged();
        OnPropertyChanged(nameof(SourceFpsDisplay));
        OnPropertyChanged(nameof(SourceDurationDisplay));
        OnPropertyChanged(nameof(PlaybackTimeDisplay));
        OnPropertyChanged(nameof(ControlSourceText));
        OnPropertyChanged(nameof(ControlHintText));
        OnPropertyChanged(nameof(IsVideoPreviewVisible));
        OnPropertyChanged(nameof(IsCameraPreviewVisible));
        OnPropertyChanged(nameof(IsCameraSessionStripVisible));
        RefreshWorkspacePresentation();
        NotifyControlCommandsChanged();
    }

    partial void OnIsRunningChanged(bool value)
    {
        RefreshRunControlPresentation(includePlayButtonText: false, refreshModelCommands: true);
        RefreshModbusLightStates();
    }

    partial void OnIsPausedChanged(bool value)
    {
        RefreshRunControlPresentation(includePlayButtonText: true, refreshModelCommands: false);
    }

    partial void OnTargetFpsTextChanged(string value)
    {
        OnPropertyChanged(nameof(TargetFpsDisplay));
        RefreshWorkspacePresentation();
    }

    partial void OnLastTransitionOkChanged(bool? value)
    {
        RefreshStatusPresentation();
    }

    partial void OnLastErrorChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
        RefreshStatusPresentation();
    }

    partial void OnStatusTextChanged(string value) => RefreshWorkspacePresentation();
    partial void OnInferenceStatusChanged(string value) => RefreshWorkspacePresentation();
    partial void OnInferenceDeviceTextChanged(string value) => RefreshWorkspacePresentation();
    partial void OnLastFrameInfoChanged(string value) => RefreshWorkspacePresentation();

    partial void OnSelectedPrimaryTaskChanged(VisionTaskDefinition? value)
    {
        var snapshot = _visionWorkspace.SelectPrimaryTask(value);
        ApplyWorkspaceCatalogSnapshot(
            snapshot,
            updateSelectedPrimaryTaskModelSource: true,
            updateSelectedTask: false,
            refreshCatalogCollections: false);
    }

    partial void OnSelectedCameraSessionChanged(CameraSessionViewModel? value)
    {
        _cameraSettings.SelectedCameraId = value?.Id ?? string.Empty;
        RefreshSelectedSessionPresentation();
    }

    partial void OnSelectedPersonnelChanged(PersonnelOptionItem? value) => RefreshPersonnelPresentation();

    [RelayCommand(CanExecute = nameof(CanApplySelectedPrimaryTask))]
    private void ApplySelectedPrimaryTask()
    {
        ApplySelectedPrimaryTaskInternal(_visionWorkspace.CurrentSnapshot.IsModelBackedPrimaryTask
            ? WorkspaceSelectionMode.ModelBackedPrimaryTask
            : WorkspaceSelectionMode.PrimaryTask);
    }

    private bool CanApplySelectedPrimaryTask()
    {
        return !Workspace.IsRunning && !AnyCameraRunning() && HasSelectedPrimaryTask;
    }

    [RelayCommand(CanExecute = nameof(CanRefreshModels))]
    private void RefreshModels()
    {
        var activeId = _visionWorkspace.CurrentSnapshot.ActivatedModelSource?.Id;
        var snapshot = LoadModelCatalog();
        if (!string.IsNullOrWhiteSpace(activeId) &&
            snapshot.AvailableModels.All(model => !string.Equals(model.Id, activeId, StringComparison.OrdinalIgnoreCase)))
        {
            ApplyWorkspaceCatalogSnapshot(
                _visionWorkspace.ClearActivatedModelSource(),
                updateSelectedPrimaryTaskModelSource: false,
                updateSelectedTask: false,
                refreshCatalogCollections: false);
            _activePrimaryBinding = null;
        }
    }

    private bool CanRefreshModels()
    {
        return !IsRunning && !AnyCameraRunning();
    }

    [RelayCommand]
    private void OpenSopConfig()
    {
        var latestSettings = _cameraSettingsRepository.Load();
        var vm = new SopConfigViewModel(
            ToSopProfiles(latestSettings.SopProfiles),
            _hardwareSettingsRepository.LoadFingerprintModules(),
            profiles => _cameraSettingsRepository.SaveSopProfiles(profiles));
        if (_dialogService.ShowSopConfig(vm))
        {
            StopAllCameraSessions();
            LoadCameraConfig();
            RestartFingerprintRecognitionHost();
        }
    }

    [RelayCommand]
    private void OpenSystemSettings()
    {
        using var vm = new SystemSettingsViewModel(
            _hardwareSettingsRepository,
            _modbusRegisterBank,
            _modbusTcpServerHost);
        if (_dialogService.ShowSystemSettings(vm))
        {
            _dbConfig = _hardwareSettingsRepository.LoadDbConfig();
            RestartRetentionCleanupService(ResultDbSession.IsInitialized
                ? ResultDbSession.ResultsDirectory
                : Path.Combine(AppContext.BaseDirectory, "results"));
            RestartFingerprintRecognitionHost();
            RestartModbusIOController();
            RestartModbusTcpServerHost();
            InferenceStatus = AnyCameraRunning()
                ? "系统设置已保存。请重启运行中的会话以应用新的 ORT 配置。"
                : "系统设置已保存。";
        }
    }

    private void OnAnalysisResult(AnalysisResult result)
    {
        _analysisResultWriter.TryEnqueue(result);

        if (!string.IsNullOrWhiteSpace(result.NgReason))
        {
            LastTransitionOk = false;
            LastError = result.NgReason;
            SetSopOutcomeNg();
            RefreshModbusLightStates();
        }

        if (result.IsTransition && result.TransitionOk.HasValue)
        {
            LastTransitionOk = result.TransitionOk.Value;
        }

        _uiDispatcher.Post(() => ApplyAnalysisStep(
            result.Step,
            result.IsReset,
            !string.IsNullOrWhiteSpace(result.NgReason) ? false : result.TransitionOk,
            !string.IsNullOrWhiteSpace(result.NgReason)));
    }

    private void ApplyAnalysisStep(int? step, bool isReset, bool? transitionOk, bool isNg = false)
    {
        if (_videoFsmStepSnapshots.Count == 0)
        {
            return;
        }

        if (isReset)
        {
            ResetVideoFsmSnapshots();
        }

        if (!step.HasValue)
        {
            if (!isNg)
            {
                ClearActiveAnalysisStep();
            }

            SyncVideoFsmItemsFromSnapshots();
            return;
        }

        var currentStep = step.Value;
        var found = false;
        foreach (var item in _videoFsmStepSnapshots)
        {
            if (item.Step == currentStep)
            {
                found = true;
                break;
            }
        }

        if (!found)
        {
            return;
        }

        if (isNg)
        {
            ApplyVideoFsmSnapshotNgStep(currentStep);
        }
        else
        {
            ApplyVideoFsmSnapshotStep(currentStep, transitionOk);
        }

        SyncVideoFsmItemsFromSnapshots();
    }

    private void ClearActiveAnalysisStep()
    {
        for (var i = 0; i < _videoFsmStepSnapshots.Count; i++)
        {
            var item = _videoFsmStepSnapshots[i];
            if (item.Status == FsmStepStatus.InProgress)
            {
                _videoFsmStepSnapshots[i] = item with { Status = FsmStepStatus.Waiting };
            }
        }
    }

    [RelayCommand]
    private void OpenActionLabel()
    {
        var defs = GetCurrentFsmStepSnapshots().Select(step => new FsmStepDefinition
        {
            Step = step.Step,
            Name = step.Name,
            ActionCode = step.ActionCode,
            TcnLabel = step.TcnLabel,
            ExpectedStateCode = step.ExpectedStateCode
        }).ToList();

        var selectedSession = !Workspace.IsVideoSource ? SelectedCameraSession : null;
        var vm = new ActionLabelViewModel(
            defs,
            _labelWriter,
            () => selectedSession?.CurrentPtsMs ?? 0,
            () => selectedSession?.CurrentSourceKey ?? string.Empty,
            () => selectedSession?.CurrentRunUuid ?? string.Empty,
            () => selectedSession?.CurrentRunStartedUtcMs ?? 0,
            () => selectedSession?.ControlState.IsRunning ?? false,
            _uiTimerFactory,
            selectedSession?.PredictionProvider ?? _tcnEngine);

        _dialogService.ShowActionLabel(vm);
    }

    [RelayCommand]
    private void OpenPerformanceDiagnostics()
    {
        _dialogService.ShowPerformanceDiagnostics(new PerformanceDiagnosticsViewModel(this));
    }

    [RelayCommand]
    private void OpenPersonnelManagement()
    {
        _dialogService.ShowPersonnelManagement(new PersonnelManagementViewModel(
            _personnelRepository,
            _authenticationService,
            () => _dialogService.ConfirmAdminPassword(_personnelRepository),
            _dialogService.RequestPersonnelPassword,
            LoadPersonnelOptions,
            _hardwareSettingsRepository.LoadFingerprintModules(),
            suspendFingerprintModuleAsync: async moduleId =>
            {
                _fingerprintRecognitionHost.Suspend(moduleId);
                await Task.CompletedTask;
            },
            resumeFingerprintModuleAsync: async moduleId =>
            {
                _fingerprintRecognitionHost.Resume(moduleId);
                await Task.CompletedTask;
            }));
    }

    [RelayCommand]
    private void OpenProductionDashboard()
    {
        _dialogService.ShowProductionDashboard(new ProductionDashboardViewModel(
            _productionDashboardQueryService,
            _personnelRepository,
            _dialogService.SaveCsvFilePath));
    }

    [RelayCommand]
    private void OpenCameraConfig()
    {
        CameraDiagnostics.Info(
            "camera-config",
            $"Opening camera settings. Cameras={_cameraSettings.Cameras.Count}");
        var latestSettings = _cameraSettingsRepository.Load();
        var vm = new CameraConfigViewModel(
            CameraConfigPath,
            latestSettings,
            _cameraProviders,
            _environmentDiagnosticsService,
            AvailableVisionTasks.ToList(),
            SelectedPrimaryTask,
            AppContext.BaseDirectory,
            () => AvailableVisionTasks.ToList(),
            () => RefreshModels(),
            ToSopProfiles(latestSettings.SopProfiles),
            settings => _cameraSettingsRepository.Save(settings));
        if (_dialogService.ShowCameraConfig(vm))
        {
            StopAllCameraSessions();
            ApplyCameraConfigPrimaryTaskSelection(vm.SavedPrimaryTask);
            LoadCameraConfig();
        }
    }

    private void ApplyCameraConfigPrimaryTaskSelection(VisionTaskDefinition? selectedTask)
    {
        if (selectedTask == null)
        {
            return;
        }

        var snapshot = _visionWorkspace.SelectPrimaryTask(selectedTask);
        ApplyWorkspaceCatalogSnapshot(
            snapshot,
            updateSelectedPrimaryTaskModelSource: true,
            updateSelectedTask: true,
            refreshCatalogCollections: false);

        // 不在此 Materialize（加载模型），当前 sessions 即将被 StopAllCameraSessions
        // + LoadCameraConfig 重建，模型加载由 PrepareCameraSessionForStart 延迟执行，
        // 避免 ONNX Runtime CUDA/TensorRT 初始化阻塞 UI 线程
        // 但需要更新 UI 状态文本，让运行概览反映当前配置
        _activePrimaryBinding = null;
        _modelActivationStatus = new ModelActivationStatusInfo(
            ModelActivationState.NoModel,
            "任务模式",
            $"{selectedTask.DisplayName} 已选择，将在相机启动时加载。",
            WorkspaceSelectionCoordinator.GetTaskDeviceText(selectedTask, BuildVisionTaskCreationContext()));
        StatusText = "Task Selected";
        InferenceStatus = $"{selectedTask.DisplayName} 已配置";
        InferenceDeviceText = _modelActivationStatus.DeviceText;
        LastError = string.Empty;
        OnPropertyChanged(nameof(ModelActivationStatus));
    }

    private void LoadPersonnelOptions()
    {
        var result = _workspaceRunCoordinator.LoadPersonnelOptions(_authenticationService.CurrentSession?.EmployeeCode);
        PersonnelOptions.Clear();
        foreach (var item in result.Options)
        {
            PersonnelOptions.Add(item);
        }

        SelectedPersonnel = result.SelectedPersonnel;
        RefreshPersonnelPresentation();
    }

    private bool EnsurePersonnelSelected()
    {
        if (!_authenticationService.IsLoggedIn)
        {
            SetTopLevelRunState(SessionRunState.Blocked);
            StatusText = "No Operator";
            InferenceStatus = "请先登录生产员工。";
            LastError = "当前未登录员工，无法启动新的作业。";
            return false;
        }

        var selection = _workspaceRunCoordinator.EnsurePersonnelSelected(SelectedPersonnel);
        if (selection.IsSelected)
        {
            return true;
        }

        SetTopLevelRunState(SessionRunState.Blocked);
        StatusText = selection.StatusText;
        InferenceStatus = selection.InferenceStatus;
        LastError = selection.LastError;
        return false;
    }

    private double ParseTargetFps()
    {
        if (double.TryParse(TargetFpsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps) && fps > 0)
        {
            return fps;
        }

        TargetFpsText = "10";
        return 10;
    }

    private void NotifyControlCommandsChanged()
    {
        PlayVideoCommand.NotifyCanExecuteChanged();
        PausePlaybackCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        StartAllCamerasCommand.NotifyCanExecuteChanged();
        StopAllCamerasCommand.NotifyCanExecuteChanged();
    }

    private void NotifySystemStatusChanged()
    {
        OnPropertyChanged(nameof(IsSystemStarting));
        OnPropertyChanged(nameof(IsSystemRunning));
        OnPropertyChanged(nameof(IsSystemIdle));
        OnPropertyChanged(nameof(SystemStatusText));
        RefreshModbusLightStates();
    }

    private void RefreshModbusLightStates()
    {
        _modbusIOController?.SetLightStates(IsSystemRunning, IsSopAlarmActive);
    }

    private void NotifyModelCommandsChanged()
    {
        ApplySelectedPrimaryTaskCommand.NotifyCanExecuteChanged();
        RefreshModelsCommand.NotifyCanExecuteChanged();
    }

    private VisionWorkspaceSnapshot LoadModelCatalog()
    {
        var currentSnapshot = _visionWorkspace.CurrentSnapshot;
        var snapshot = _visionWorkspace.ReloadCatalog(
            currentSnapshot.PrimaryTaskModelSource?.Id,
            currentSnapshot.SelectedPrimaryTask?.Id);
        ApplyWorkspaceCatalogSnapshot(
            snapshot,
            updateSelectedPrimaryTaskModelSource: true,
            updateSelectedTask: true,
            refreshCatalogCollections: true);
        return snapshot;
    }

    private void ImportLegacyConfigsToSqlite(string dbPath)
    {
        if (_hardwareSettingsRepository.HasGlobalConfig())
            return;

        var appConfigPath = Path.Combine(AppContext.BaseDirectory, "app_config.json");
        if (!File.Exists(appConfigPath))
        {
            XTrace.WriteLine("[Config] No app_config.json found — using defaults.");
            _hardwareSettingsRepository.SaveGlobalConfig(new DbConfig(), new AnalysisConfig());
            return;
        }

        try
        {
            XTrace.WriteLine("[Config] Importing legacy app_config.json into SQLite.");
            var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
            var json = File.ReadAllText(appConfigPath);
            var appConfig = JsonSerializer.Deserialize<AppConfig>(json, options);
            if (appConfig == null)
            {
                XTrace.WriteLine("[Config] Failed to parse app_config.json — using defaults.");
                _hardwareSettingsRepository.SaveGlobalConfig(new DbConfig(), new AnalysisConfig());
                return;
            }

            _hardwareSettingsRepository.ImportFromAppConfigIfEmpty(appConfig);
            _cameraSettingsRepository.ImportFromFileIfEmpty(CameraConfigPath);
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Error("config", $"Failed to import legacy configs: {ex.Message}");
            _hardwareSettingsRepository.SaveGlobalConfig(new DbConfig(), new AnalysisConfig());
        }
    }

    private void ReloadAppConfigurationState()
    {
        XTrace.WriteLine("[MainViewModel] Reloading app configuration state.");
        _dbConfig = _hardwareSettingsRepository.LoadDbConfig();
        _analysisConfig = _hardwareSettingsRepository.LoadAnalysisConfig();
        RestartRetentionCleanupService(ResultDbSession.IsInitialized
            ? ResultDbSession.ResultsDirectory
            : Path.Combine(AppContext.BaseDirectory, "results"));
        var defaultSteps = ToSopProfiles(_cameraSettings.SopProfiles).FirstOrDefault()?.Steps ?? new List<FsmStepDefinition>();
        ApplySopRuntimeDefinitions(defaultSteps, applySessionFsmDefinitions: true);
        ApplySelectedCameraWorkspaceState();
        RestartFingerprintRecognitionHost();
        RestartModbusIOController();
        RestartModbusTcpServerHost();
    }

    private void RestartRetentionCleanupService(string resultsDirectory)
    {
        _retentionCleanupService?.Dispose();
        _retentionCleanupService = new RetentionCleanupService(BuildRetentionCleanupOptions(_dbConfig, resultsDirectory));
        _retentionCleanupService.Start();
    }

    private RetentionCleanupOptions BuildRetentionCleanupOptions(DbConfig dbConfig, string resultsDirectory)
    {
        var recordingRoots = _cameraSettings.Cameras
            .Select(camera => camera.RecordingRootDirectory)
            .Append("Recordings")
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RetentionCleanupOptions
        {
            ResultsDirectory = resultsDirectory,
            RecordingDirectories = recordingRoots,
            InspectionImageDirectories = ["InspectionImages"],
            RetentionDays = dbConfig.RetentionDays,
            EnableAutoCleanup = dbConfig.EnableAutoCleanup
        };
    }

    private void RestartFingerprintRecognitionHost()
    {
        NormalizePersonnelFingerprintBindings();
        var runtimeModules = FingerprintRuntimeModuleSelector.SelectRuntimeModules(
            _hardwareSettingsRepository.LoadFingerprintModules(),
            ToSopProfiles(_cameraSettings.SopProfiles));
        _fingerprintRecognitionHost.Start(runtimeModules);
        if (_fingerprintRecognitionHost.RunningModuleCount > 0)
        {
            XTrace.WriteLine("[Fingerprint] Started {0} fingerprint module monitor(s).", _fingerprintRecognitionHost.RunningModuleCount);
            InferenceStatus = $"已启动 {_fingerprintRecognitionHost.RunningModuleCount} 个指纹模块监听";
        }
        else
        {
            XTrace.WriteLine("[Fingerprint] No enabled or SOP-bound fingerprint module monitor was started.");
        }
    }

    private void NormalizePersonnelFingerprintBindings()
    {
        _personnelRepository.NormalizeFingerprintBindingsForModules(
            _hardwareSettingsRepository.LoadFingerprintModules().Select(module => module.Id));
    }

    private void RestartModbusIOController()
    {
        var modbusModules = _hardwareSettingsRepository.LoadModbusModules();
        var enabledModules = modbusModules
            .Select(m => m.Normalize())
            .Where(m => m.Enabled && m.Lights.Count > 0)
            .ToList();

        _modbusIOController.Stop();
        _modbusIOController.Start(enabledModules);

        // Write initial state immediately
        RefreshModbusLightStates();

        if (enabledModules.Count > 0)
        {
            XTrace.WriteLine("[ModbusIO] Started {0} Modbus IO module(s) with {1} light binding(s).",
                enabledModules.Count,
                enabledModules.Sum(m => m.Lights.Count));
            InferenceStatus = $"已启动 {enabledModules.Count} 个 Modbus IO 模块";
        }
        else
        {
            XTrace.WriteLine("[ModbusIO] No enabled Modbus IO modules found. total={0}, enabledWithLights={1}",
                modbusModules.Count,
                modbusModules.Count(m => m.Enabled && m.Lights.Count > 0));
        }
    }

    private void OnFingerprintError(string message)
    {
        _uiDispatcher.Post(() =>
        {
            LastError = message;
            InferenceStatus = message;
        });
    }

    private void OnFingerprintRecognized(FingerprintPersonnelRecognition recognition)
    {
        _uiDispatcher.Post(() => ApplyFingerprintRecognition(recognition));
    }

    private void ApplyFingerprintRecognition(FingerprintPersonnelRecognition recognition)
    {
        if (recognition.Personnel == null)
        {
            InferenceStatus = $"{recognition.ModuleName} 识别到未绑定指纹ID {recognition.Result.FingerprintId}";
            return;
        }

        InferenceStatus = $"{recognition.ModuleName} 识别到 {recognition.Personnel.EmployeeName}，当前登录员工保持为 {CurrentLoginDisplayText}";

        var resetSession = ResolveFingerprintResetSession(recognition);
        if (resetSession != null)
        {
            resetSession.ResetSopFault(new SopFaultResetContext(
                "fingerprint",
                recognition.ModuleId,
                recognition.ModuleName,
                recognition.Result.FingerprintId,
                recognition.Personnel.EmployeeCode,
                recognition.Personnel.EmployeeName,
                ResolveActiveAlarmEventUuid(resetSession),
                "fingerprint reset"));
            if (resetSession == SelectedCameraSession)
            {
                ApplySelectedCameraWorkspaceState();
                SetSopOutcomeWaiting();
            }

            InferenceStatus = $"{recognition.ModuleName} 已识别：{recognition.Personnel.EmployeeName} ({recognition.Personnel.EmployeeCode})，已复位 {resetSession.Name} SOP 报警";
        }
        else
        {
            InferenceStatus = $"{recognition.ModuleName} 已识别：{recognition.Personnel.EmployeeName} ({recognition.Personnel.EmployeeCode})";
        }

        RefreshPersonnelPresentation();
    }

    private CameraSessionViewModel? ResolveFingerprintResetSession(FingerprintPersonnelRecognition recognition)
    {
        if (SelectedCameraSession?.CanResetSopFault == true &&
            IsFingerprintAllowedForSessionSop(SelectedCameraSession, recognition.ModuleId))
        {
            return SelectedCameraSession;
        }

        var faultedSessions = CameraSessions
            .Where(item => item.CanResetSopFault)
            .Where(item => IsFingerprintAllowedForSessionSop(item, recognition.ModuleId))
            .ToList();

        return faultedSessions.Count switch
        {
            0 => null,
            1 => faultedSessions[0],
            _ => faultedSessions.FirstOrDefault(item => item.IsRunning) ?? faultedSessions[0]
        };
    }

    private string? ResolveActiveAlarmEventUuid(CameraSessionViewModel session)
    {
        return _activeSopAlarmEventUuids.TryGetValue(session.Id, out var alarmEventUuid)
            ? alarmEventUuid
            : null;
    }

    private bool IsFingerprintAllowedForSessionSop(CameraSessionViewModel session, string moduleId)
    {
        var sopProfileId = session.Profile.SelectedSopProfileId;
        var sopProfile = !string.IsNullOrWhiteSpace(sopProfileId)
            ? ToSopProfiles(_cameraSettings.SopProfiles).FirstOrDefault(item =>
                string.Equals(item.Id, sopProfileId, StringComparison.OrdinalIgnoreCase))
            : null;
        var configuredModuleId = sopProfile?.FingerprintModuleId;
        return string.IsNullOrWhiteSpace(configuredModuleId) ||
               string.Equals(configuredModuleId, moduleId, StringComparison.OrdinalIgnoreCase);
    }

    private void AttachSopFaultEventHandlers()
    {
        foreach (var session in CameraSessions)
        {
            session.SopFaultAlarmRaised -= OnSopFaultAlarmRaised;
            session.SopFaultAlarmRaised += OnSopFaultAlarmRaised;
            session.SopFaultReset -= OnSopFaultReset;
            session.SopFaultReset += OnSopFaultReset;
        }
    }

    private void OnSopFaultAlarmRaised(SopFaultAlarmEvent alarm)
    {
        RefreshModbusLightStates();

        try
        {
            _sopAlarmEventRepository.Insert(new SopAlarmEventRecord(
                alarm.EventUuid,
                SopAlarmEventTypes.Alarm,
                alarm.RunUuid,
                alarm.Session.Id,
                alarm.Session.Name,
                alarm.SourceKey,
                alarm.Step,
                alarm.NgReason,
                string.Empty,
                string.Empty,
                string.Empty,
                null,
                string.Empty,
                string.Empty,
                alarm.EventUtcMs,
                string.Empty,
                string.Empty));
            _activeSopAlarmEventUuids[alarm.Session.Id] = alarm.EventUuid;
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Error("sop", "Failed to persist SOP alarm event.", ex);
            LastError = $"SOP 报警事件写入失败：{ex.Message}";
            InferenceStatus = LastError;
        }
    }

    private void OnSopFaultReset(SopFaultResetEvent reset)
    {
        try
        {
            var relatedAlarmEventUuid = reset.Context.RelatedAlarmEventUuid
                                        ?? ResolveActiveAlarmEventUuid(reset.Session)
                                        ?? string.Empty;
            _sopAlarmEventRepository.Insert(new SopAlarmEventRecord(
                reset.EventUuid,
                SopAlarmEventTypes.Reset,
                reset.RunUuid,
                reset.Session.Id,
                reset.Session.Name,
                reset.SourceKey,
                reset.Step,
                reset.NgReason,
                reset.Context.ResetSource,
                reset.Context.FingerprintModuleId ?? string.Empty,
                reset.Context.FingerprintModuleName ?? string.Empty,
                reset.Context.FingerprintId,
                reset.Context.EmployeeCode ?? string.Empty,
                reset.Context.EmployeeName ?? string.Empty,
                reset.EventUtcMs,
                relatedAlarmEventUuid,
                reset.Context.Note ?? string.Empty));
            _activeSopAlarmEventUuids.Remove(reset.Session.Id);
            RefreshModbusLightStates();
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Error("sop", "Failed to persist SOP reset event.", ex);
            LastError = $"SOP 复位事件写入失败：{ex.Message}";
            InferenceStatus = LastError;
        }
    }

    private void LoadCameraConfig()
    {
        XTrace.WriteLine("[MainViewModel] Loading camera config from inference.db");
        var cameraSettings = _cameraSettingsRepository.Load();
        var result = _cameraSessionWorkspaceCoordinator.LoadCameraWorkspace(
            cameraSettings,
            CameraSessions,
            _analysisConfig,
            ToSopProfiles(cameraSettings.SopProfiles),
            _activePrimaryBinding,
            SelectedPrimaryTask,
            BuildVisionTaskCreationContext(),
            PrepareCameraSessionForStart,
            OnCameraSessionPropertyChanged,
            Workspace.IsVideoSource,
            GetModelBinding);
        _cameraSettings = result.CameraSettings;
        _sopProfiles = ToSopProfiles(_cameraSettings.SopProfiles);
        ApplySopRuntimeDefinitions(_sopProfiles.FirstOrDefault()?.Steps ?? new List<FsmStepDefinition>());
        SelectedCameraSession = result.SelectedSession;
        ApplySelectedCameraPrimaryTaskSelection();
        AttachSopFaultEventHandlers();

        NotifyCameraSessionCollectionChanged();
        if (!Workspace.IsVideoSource || result.AutoStartedSessions)
        {
            ApplySelectedCameraWorkspaceState();
        }

        RestartRetentionCleanupService(ResultDbSession.IsInitialized
            ? ResultDbSession.ResultsDirectory
            : Path.Combine(AppContext.BaseDirectory, "results"));
    }

    private void ApplySelectedCameraPrimaryTaskSelection()
    {
        var taskId = SelectedCameraSession?.Profile.PrimaryTaskId;
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        var task = AvailableVisionTasks.FirstOrDefault(item =>
            string.Equals(item.Id, taskId, StringComparison.OrdinalIgnoreCase));
        ApplyCameraConfigPrimaryTaskSelection(task);
    }

    private void RestartModbusTcpServerHost()
    {
        var status = _modbusTcpServerHost.Restart(_dbConfig.ModbusTcpServer);
        if (!_dbConfig.ModbusTcpServer.Enabled)
        {
            XTrace.WriteLine("[ModbusServer] Disabled.");
            return;
        }

        if (status.IsRunning)
        {
            XTrace.WriteLine("[ModbusServer] Listening on {0}.", status.Endpoint);
            InferenceStatus = $"ModbusTCP 服务端监听 {status.Endpoint}";
        }
        else
        {
            XTrace.WriteLine("[ModbusServer] Failed to listen on {0}: {1}", status.Endpoint, status.Message);
            LastError = $"ModbusTCP 服务端启动失败：{status.Message}";
        }
    }

    private void OnCameraSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CameraSessionViewModel session)
        {
            return;
        }

        var effects = _cameraSessionLifecycleCoordinator.HandlePropertyChanged(
            session,
            sender == SelectedCameraSession,
            e.PropertyName,
            SelectedPersonnel);

        if (effects.RefreshSelectedWorkspaceState)
        {
            ApplySelectedCameraWorkspaceState();
        }

        if (sender == SelectedCameraSession &&
            (e.PropertyName == nameof(CameraSessionViewModel.SopOutcomeText) ||
             e.PropertyName == nameof(CameraSessionViewModel.SopOutcomeBackground) ||
             e.PropertyName == nameof(CameraSessionViewModel.SopOutcomeForeground) ||
             e.PropertyName == nameof(CameraSessionViewModel.CanResetSopFault)))
        {
            SyncSopOutcomeFromSelectedSession();
        }

        if (effects.RefreshPersonnelDisplay)
        {
            OnPropertyChanged(nameof(CurrentSessionPersonnelDisplayText));
        }

        if (effects.RefreshWorkspaceProjection)
        {
            RefreshWorkspacePresentation();
        }

        if (effects.RefreshModelCommands)
        {
            NotifyModelCommandsChanged();
        }

        if (effects.RefreshControlCommands)
        {
            NotifyControlCommandsChanged();
        }

        if (e.PropertyName == nameof(CameraSessionViewModel.IsRunning) ||
            e.PropertyName == nameof(CameraSessionViewModel.RunState))
        {
            StartAllCamerasCommand.NotifyCanExecuteChanged();
            StopAllCamerasCommand.NotifyCanExecuteChanged();
            NotifySystemStatusChanged();
        }
    }

    private PrimaryVisionTaskBinding? GetModelBinding(CameraSessionViewModel session)
    {
        var task = ResolvePrimaryTaskForSession(session);
        if (task == null) return null;
        if (string.Equals(session.ActivePrimaryTaskId, task.Id, StringComparison.OrdinalIgnoreCase))
            return null;
        return _sessionTaskOrchestrator.CreatePrimaryTaskBinding(task, BuildVisionTaskCreationContext());
    }

    private SessionStartPrecheckResult PrepareCameraSessionForStart(CameraSessionViewModel session)
    {
        var preparation = new CameraSessionStartPreparationResult(
            session,
            FastCheckTaskExists(session));
        return ApplyCameraSessionStartPreparation(preparation);
    }

    private SessionStartPrecheckResult FastCheckTaskExists(CameraSessionViewModel session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var task = ResolvePrimaryTaskForSession(session);
        if (task == null)
        {
            var configuredTaskId = session.Profile.PrimaryTaskId;
            var message = string.IsNullOrWhiteSpace(configuredTaskId)
                ? $"No vision task is configured for {session.Name}."
                : $"Configured vision task '{configuredTaskId}' was not found for {session.Name}.";
            return new SessionStartPrecheckResult(
                SessionStartPrecheckState.NoModel,
                message);
        }

        return SessionStartPrecheckResult.Success;
    }

    private VisionTaskDefinition? ResolvePrimaryTaskForSession(CameraSessionViewModel session)
    {
        var taskId = session.Profile.PrimaryTaskId;
        if (!string.IsNullOrWhiteSpace(taskId))
        {
            return AvailableVisionTasks.FirstOrDefault(task =>
                string.Equals(task.Id, taskId, StringComparison.OrdinalIgnoreCase));
        }

        return SelectedPrimaryTask ?? _visionWorkspace.CurrentSnapshot.SelectedPrimaryTask;
    }

    private void ApplySelectedCameraWorkspaceState()
    {
        var selectionState = _cameraSessionWorkspaceCoordinator.BuildSelectionState(
            SelectedCameraSession,
            _videoFsmSteps);
        FsmSteps = selectionState.FsmSteps;
        SyncSopOutcomeFromSelectedSession();
        RefreshWorkspacePresentation();
    }

    private void SyncSopOutcomeFromSelectedSession()
    {
        if (SelectedCameraSession == null)
        {
            return;
        }

        SopOutcomeText = SelectedCameraSession.SopOutcomeText;
        SopOutcomeBackground = SelectedCameraSession.SopOutcomeBackground;
        SopOutcomeForeground = SelectedCameraSession.SopOutcomeForeground;
        CanResetSopFault = SelectedCameraSession.CanResetSopFault;
        ResetSopFaultCommand.NotifyCanExecuteChanged();
    }

    private bool ApplyInteractiveSessionLaunchResult(
        InteractiveSessionLaunchResult result,
        bool refreshControlCommands)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Session != null)
        {
            SelectedCameraSession = result.Session;
            ApplySelectedCameraWorkspaceState();
        }

        if (!result.Success)
        {
            if (result.Session == null)
            {
                SetTopLevelRunState(SessionRunState.NoSource);
                StatusText = result.StatusText ?? "Error";
                InferenceStatus = result.InferenceStatus ?? "Interactive session launch failed.";
                LastError = result.LastError ?? string.Empty;
            }

            return false;
        }

        if (refreshControlCommands)
        {
            NotifyControlCommandsChanged();
        }

        return true;
    }

    private SessionStartPrecheckResult ApplyCameraSessionStartPreparation(
        CameraSessionStartPreparationResult preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        if (!preparation.PrecheckResult.IsSuccess)
        {
            SetTopLevelRunState(preparation.PrecheckResult.State switch
            {
                SessionStartPrecheckState.NoModel => SessionRunState.NoModel,
                SessionStartPrecheckState.Error => SessionRunState.Error,
                _ => SessionRunState.Blocked
            });
            var message = preparation.PrecheckResult.Message ?? InferenceStatus;
            LastError = message ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(message))
            {
                InferenceStatus = message;
            }

            return preparation.PrecheckResult;
        }

        if (preparation.SelectedSession != null)
        {
            SelectedCameraSession = preparation.SelectedSession;
            ApplySelectedCameraWorkspaceState();
        }

        return preparation.PrecheckResult;
    }

    private IReadOnlyList<FsmStepSnapshot> GetCurrentFsmStepSnapshots()
    {
        return SelectedCameraSession?.FsmStepSnapshots ?? _videoFsmStepSnapshots;
    }

    private void ApplySopRuntimeDefinitions(
        IReadOnlyList<FsmStepDefinition> defaultSteps,
        bool applySessionFsmDefinitions = false)
    {
        _analysisEngine?.UpdateFsmDefinitions(defaultSteps);

        _videoFsmStepSnapshots.Clear();
        foreach (var snapshot in defaultSteps.Select(FsmStepSnapshot.FromDefinition))
        {
            _videoFsmStepSnapshots.Add(snapshot);
        }

        SyncVideoFsmItemsFromSnapshots();

        if (applySessionFsmDefinitions)
        {
            _cameraSessionWorkspaceCoordinator.ApplyFsmDefinitions(CameraSessions, defaultSteps);
        }
    }

    private void SyncVideoFsmItemsFromSnapshots()
    {
        var timelineOriginUtc = FsmStepItem.ResolveTimelineOrigin(_videoFsmStepSnapshots);
        _videoFsmSteps.Clear();
        foreach (var snapshot in _videoFsmStepSnapshots)
        {
            _videoFsmSteps.Add(FsmStepItem.FromSnapshot(snapshot, timelineOriginUtc));
        }
    }

    private void RefreshSopTimelineDisplays()
    {
        foreach (var item in FsmSteps)
        {
            item.RefreshLiveTimes();
        }
    }

    private void ResetVideoFsmSnapshots()
    {
        for (var i = 0; i < _videoFsmStepSnapshots.Count; i++)
        {
            var current = _videoFsmStepSnapshots[i];
            _videoFsmStepSnapshots[i] = current with
            {
                Status = FsmStepStatus.Waiting,
                StartTimeUtc = null,
                EndTimeUtc = null,
                Duration = null,
                IsNg = false
            };
        }
    }

    private void ApplyVideoFsmSnapshotStep(int step, bool? transitionOk)
    {
        var nextStep = _videoFsmStepSnapshots
            .Where(item => item.Step > step)
            .Select(item => (int?)item.Step)
            .FirstOrDefault();

        for (var i = 0; i < _videoFsmStepSnapshots.Count; i++)
        {
            var current = _videoFsmStepSnapshots[i];
            if (current.Step <= step)
            {
                var endTime = current.EndTimeUtc ?? DateTimeOffset.UtcNow;
                var duration = current.StartTimeUtc.HasValue ? endTime - current.StartTimeUtc.Value : current.Duration;
                _videoFsmStepSnapshots[i] = current with
                {
                    Status = FsmStepStatus.Done,
                    StartTimeUtc = current.StartTimeUtc ?? DateTimeOffset.UtcNow,
                    EndTimeUtc = endTime,
                    Duration = duration
                };
            }
            else if (current.Step == nextStep)
            {
                if (transitionOk == false)
                {
                    var endTime = current.EndTimeUtc ?? DateTimeOffset.UtcNow;
                    var duration = current.StartTimeUtc.HasValue ? endTime - current.StartTimeUtc.Value : current.Duration;
                    _videoFsmStepSnapshots[i] = current with
                    {
                        Status = FsmStepStatus.Done,
                        StartTimeUtc = current.StartTimeUtc ?? DateTimeOffset.UtcNow,
                        EndTimeUtc = endTime,
                        Duration = duration,
                        IsNg = true
                    };
                    continue;
                }

                _videoFsmStepSnapshots[i] = current with
                {
                    Status = FsmStepStatus.InProgress,
                    StartTimeUtc = current.StartTimeUtc ?? DateTimeOffset.UtcNow,
                    IsNg = transitionOk.HasValue && !transitionOk.Value
                };
            }
            else
            {
                _videoFsmStepSnapshots[i] = current with { Status = FsmStepStatus.Waiting };
            }
        }

        if (!CameraSessions.Any(session => session.CanResetSopFault) &&
            transitionOk != false &&
            _videoFsmStepSnapshots.Count > 0 &&
            step >= _videoFsmStepSnapshots.Max(item => item.Step))
        {
            SetSopOutcomeOk();
        }
    }

    private void ApplyVideoFsmSnapshotNgStep(int step)
    {
        var found = _videoFsmStepSnapshots.Any(item => item.Step == step);
        if (!found)
        {
            return;
        }

        for (var i = 0; i < _videoFsmStepSnapshots.Count; i++)
        {
            var current = _videoFsmStepSnapshots[i];
            if (current.Step < step)
            {
                var endTime = current.EndTimeUtc ?? DateTimeOffset.UtcNow;
                var duration = current.StartTimeUtc.HasValue ? endTime - current.StartTimeUtc.Value : current.Duration;
                _videoFsmStepSnapshots[i] = current with
                {
                    Status = FsmStepStatus.Done,
                    StartTimeUtc = current.StartTimeUtc ?? DateTimeOffset.UtcNow,
                    EndTimeUtc = endTime,
                    Duration = duration,
                    IsNg = false
                };
            }
            else if (current.Step == step)
            {
                var endTime = current.EndTimeUtc ?? DateTimeOffset.UtcNow;
                var duration = current.StartTimeUtc.HasValue ? endTime - current.StartTimeUtc.Value : current.Duration;
                _videoFsmStepSnapshots[i] = current with
                {
                    Status = FsmStepStatus.Done,
                    StartTimeUtc = current.StartTimeUtc ?? DateTimeOffset.UtcNow,
                    EndTimeUtc = endTime,
                    Duration = duration,
                    IsNg = true
                };
            }
            else if (current.Status == FsmStepStatus.InProgress || current.IsNg)
            {
                _videoFsmStepSnapshots[i] = current with
                {
                    Status = FsmStepStatus.Waiting,
                    IsNg = false
                };
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanResetSopFault))]
    public void ResetSopFault()
    {
        if (CurrentSession != null)
        {
            CurrentSession.ResetSopFault();
            ApplySelectedCameraWorkspaceState();
            SetSopOutcomeWaiting();
            return;
        }

        _analysisEngine?.ResetAnalysis();
        LastError = string.Empty;
        LastTransitionOk = null;
        SetSopOutcomeWaiting();
        ResetVideoFsmSnapshots();
        SyncVideoFsmItemsFromSnapshots();
        NotifyWorkspaceProjectionChanged();
        RefreshModbusLightStates();
    }

    private void SetSopOutcomeWaiting()
    {
        SopOutcomeText = CurrentSession?.SopOutcomeText ?? "等待";
        SopOutcomeBackground = CurrentSession?.SopOutcomeBackground ?? "#EDF2F7";
        SopOutcomeForeground = CurrentSession?.SopOutcomeForeground ?? "#607080";
        CanResetSopFault = CurrentSession?.CanResetSopFault ?? false;
        ResetSopFaultCommand.NotifyCanExecuteChanged();
        RefreshModbusLightStates();
    }

    private void SetSopOutcomeOk()
    {
        SopOutcomeText = "OK";
        SopOutcomeBackground = "#2E7D4F";
        SopOutcomeForeground = "White";
        CanResetSopFault = false;
        ResetSopFaultCommand.NotifyCanExecuteChanged();
        RefreshModbusLightStates();
    }

    private void SetSopOutcomeNg()
    {
        SopOutcomeText = "NG";
        SopOutcomeBackground = "#A83228";
        SopOutcomeForeground = "White";
        CanResetSopFault = true;
        ResetSopFaultCommand.NotifyCanExecuteChanged();
        RefreshModbusLightStates();
    }

    private void StopAllCameraSessions()
    {
        _sessionTaskOrchestrator.StopAllSessions(CameraSessions);
    }

    private bool AnyCameraRunning()
    {
        return CameraSessions.Any(session => session.IsRunning);
    }

    private void StartConfiguredCameraProfiles(IEnumerable<CameraProfile> profiles)
    {
        _cameraSessionWorkspaceCoordinator.StartConfiguredSessions(profiles, CameraSessions);

        if (SelectedCameraSession != null)
        {
            ApplySelectedCameraWorkspaceState();
        }
    }

    private void NotifyCameraSessionCollectionChanged() => RefreshCameraSessionCollectionPresentation();

    private WorkspaceProjectionSnapshot BuildWorkspaceProjection()
    {
        var currentSession = CurrentSession;
        var activeBindingEmployeeName =
            currentSession != null && _workspaceRunCoordinator.TryGetActiveBinding(currentSession.Id, out var activeBinding)
                ? activeBinding.EmployeeName
                : null;

        return _workspaceProjectionBuilder.Build(new WorkspaceProjectionRequest(
            currentSession,
            WorkspaceStatusSnapshot,
            VideoFrame,
            StatusText,
            SourceLabel,
            InferenceStatus,
            InferenceDeviceText,
            LastFrameInfo,
            LastError,
            ControlSourceText,
            ControlHintText,
            TargetFpsDisplay,
            CaptureFps,
            SourceFpsDisplay,
            SourceDurationDisplay,
            PlaybackTimeDisplay,
            CurrentTimeText,
            DroppedByPts,
            DroppedByCaptureQueue,
            DroppedByInferDrain,
            DroppedByRenderQueue,
            DroppedByRenderDrain,
            FsmSteps,
            IsVideoSource,
            IsRunning,
            IsPaused,
            HasCameraSessions,
            SelectedPersonnel,
            CurrentSessionPersonnelDisplayText,
            activeBindingEmployeeName));
    }

    private SessionStatusSnapshot BuildStatusSnapshot()
    {
        if (CurrentSession != null)
        {
            return CurrentSession.StatusSnapshot;
        }

        var transitionState = LastTransitionOk switch
        {
            true => SessionTransitionState.Normal,
            false => SessionTransitionState.Abnormal,
            _ => SessionTransitionState.Unknown
        };

        return new SessionStatusSnapshot(_topLevelRunState, IsVideoSource, transitionState, LastError);
    }

    private static string GetRunStateText(SessionStatusSnapshot snapshot)
    {
        return SessionStatusTextFormatter.GetRunStateText(snapshot);
    }

    private static string GetTransitionStatusText(SessionStatusSnapshot snapshot)
    {
        return SessionStatusTextFormatter.GetTransitionStatusText(snapshot);
    }

    private static string GetStatusBadgeText(SessionStatusSnapshot snapshot)
    {
        return SessionStatusTextFormatter.GetStatusBadgeText(snapshot);
    }

    private bool GetPreferredVideoPtsMode()
    {
        return SelectedCameraSession?.Profile.UseSourcePtsForVideo
               ?? _cameraSettings.Cameras.FirstOrDefault()?.UseSourcePtsForVideo
               ?? true;
    }

    private VisionTaskCreationContext BuildVisionTaskCreationContext()
    {
        return new VisionTaskCreationContext(
            _deviceKind,
            _confidenceThreshold,
            _nmsThreshold);
    }

    private void UpdateSelectionFromWorkspace(
        VisionWorkspaceSnapshot snapshot,
        bool updateSelectedPrimaryTaskModelSource,
        bool updateSelectedTask)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (updateSelectedPrimaryTaskModelSource &&
            !string.Equals(SelectedModel?.Id, snapshot.PrimaryTaskModelSource?.Id, StringComparison.OrdinalIgnoreCase))
        {
            SelectedModel = snapshot.PrimaryTaskModelSource;
        }

        if (updateSelectedTask &&
            !string.Equals(SelectedPrimaryTask?.Id, snapshot.SelectedPrimaryTask?.Id, StringComparison.OrdinalIgnoreCase))
        {
            SelectedPrimaryTask = snapshot.SelectedPrimaryTask;
        }
    }

    private void ApplyWorkspaceCatalogSnapshot(
        VisionWorkspaceSnapshot snapshot,
        bool updateSelectedPrimaryTaskModelSource,
        bool updateSelectedTask,
        bool refreshCatalogCollections)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (refreshCatalogCollections)
        {
            SyncWorkspaceCatalogCollections(snapshot);
        }

        UpdateSelectionFromWorkspace(snapshot, updateSelectedPrimaryTaskModelSource, updateSelectedTask);
        ModelCatalogStatusText = snapshot.WorkspaceStatusInfo.Detail;
        OnPropertyChanged(nameof(ModelWorkspaceStatus));
        NotifyPrimaryTaskModelSourcePresentationChanged();
        NotifySelectedPrimaryTaskPresentationChanged();
        NotifyModelCommandsChanged();
    }

    private void SetTopLevelRunState(SessionRunState runState)
    {
        if (_topLevelRunState == runState)
        {
            return;
        }

        _topLevelRunState = runState;
        NotifyStatusSnapshotChanged();
    }

    private void NotifyStatusSnapshotChanged()
    {
        OnPropertyChanged(nameof(StatusSnapshot));
        OnPropertyChanged(nameof(WorkspaceStatusSnapshot));
        OnPropertyChanged(nameof(RunStateText));
        OnPropertyChanged(nameof(TransitionStatusText));
        OnPropertyChanged(nameof(StatusBadgeText));
    }

    private static SessionRunState ResolveTopLevelRunState(WorkspaceSelectionMaterializationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Success)
        {
            return result.ModelBackedActivationAttempt != null
                ? SessionRunState.ModelSelected
                : SessionRunState.Idle;
        }

        return result.FailureState switch
        {
            SessionStartPrecheckState.NoModel => SessionRunState.NoModel,
            SessionStartPrecheckState.Error => SessionRunState.Error,
            _ => SessionRunState.Blocked
        };
    }

    private void SyncWorkspaceCatalogCollections(VisionWorkspaceSnapshot snapshot)
    {
        AvailableModels.Clear();
        foreach (var model in snapshot.AvailableModels)
        {
            AvailableModels.Add(model);
        }

        AvailableVisionTasks.Clear();
        foreach (var task in snapshot.AvailableVisionTasks)
        {
            AvailableVisionTasks.Add(task);
        }
    }

    private void NotifyPrimaryTaskModelSourcePresentationChanged()
    {
        OnPropertyChanged(nameof(PrimaryTaskModelSourceText));
        OnPropertyChanged(nameof(PrimaryTaskModelSourcePathText));
        OnPropertyChanged(nameof(PrimaryTaskModelSourceDescriptionText));
    }

    private void NotifySelectedPrimaryTaskPresentationChanged()
    {
        OnPropertyChanged(nameof(SelectedPrimaryTask));
        OnPropertyChanged(nameof(SelectedPrimaryTaskText));
        OnPropertyChanged(nameof(SelectedPrimaryTaskKindText));
        OnPropertyChanged(nameof(SelectedPrimaryTaskRuntimeText));
        OnPropertyChanged(nameof(HasSelectedPrimaryTask));
    }

    private void ApplySelectedPrimaryTaskInternal(WorkspaceSelectionMode mode)
    {
        MaterializeWorkspaceSelection(mode);
    }

    private static IReadOnlyList<SopProfile> ToSopProfiles(IEnumerable<CameraSopProfile> profiles)
    {
        return profiles
            .Select(profile => new SopProfile
            {
                Id = profile.Id,
                Name = profile.Name,
                Strategy = profile.Strategy,
                FingerprintModuleId = profile.FingerprintModuleId,
                Steps = profile.Steps
                    .Select(step => new FsmStepDefinition
                    {
                        Step = step.Step,
                        Name = step.Name,
                        ActionCode = step.ActionCode,
                        TcnLabel = step.TcnLabel,
                        ExpectedStateCode = step.ExpectedStateCode
                    })
                    .ToList()
            })
            .ToList();
    }
}
