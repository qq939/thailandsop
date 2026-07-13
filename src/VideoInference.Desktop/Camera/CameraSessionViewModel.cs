using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using NewLife.Log;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VideoInferenceDemo;

public sealed partial class CameraSessionViewModel : ObservableObject, IDisposable, IVisionTaskBindingTarget
{
    private readonly CameraProviderRegistry _cameraProviders;
    private readonly VideoPipeline _pipeline;
    private readonly SqliteResultWriter _resultWriter;
    private readonly SqliteAnalysisResultWriter _analysisResultWriter;
    private readonly TcnLabelWriter _labelWriter;
    private readonly TcnFeatureWriter? _featureWriter;
    private readonly AnalysisEngine? _analysisEngine;
    private readonly TcnOnnxInferenceEngine? _tcnEngine;
    private readonly TcnPredictionRecorder? _tcnPredictionRecorder;
    private readonly PipelineSessionController _sessionController;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IDesktopPipelineSupportFactory _pipelineSupportFactory;
    private readonly WpfFramePresenter _framePresenter;
    private readonly Func<CameraSessionViewModel, SessionStartPrecheckResult>? _prepareStart;
    private readonly Func<CameraSessionViewModel, PrimaryVisionTaskBinding?>? _getBinding;
    private readonly IModbusHoldingRegisterAccessor _modbusRegisters;
    private readonly VisionTaskFactoryRegistry _visionTaskFactoryRegistry;
    private readonly List<FsmStepDefinition> _fsmDefinitions = new();
    private readonly List<FsmStepSnapshot> _fsmStepSnapshots = new();
    private bool _sopFaultActive;
    private long _currentPtsMs;
    private string _lastVideoPath = string.Empty;
    private SessionStatusSnapshot _statusSnapshot = SessionStatusSnapshot.Empty;

    public CameraSessionViewModel(
        CameraProfile profile,
        CameraProviderRegistry cameraProviders,
        SqliteResultWriter resultWriter,
        SqliteAnalysisResultWriter analysisResultWriter,
        TcnLabelWriter labelWriter,
        TcnFeatureWriter? featureWriter,
        AnalysisConfig analysisConfig,
        IReadOnlyList<FsmStepDefinition> fsmDefinitions,
        IUiDispatcher uiDispatcher,
        IDesktopPipelineSupportFactory pipelineSupportFactory,
        VisionTaskFactoryRegistry visionTaskFactoryRegistry,
        Func<CameraSessionViewModel, SessionStartPrecheckResult>? prepareStart = null,
        Func<CameraSessionViewModel, PrimaryVisionTaskBinding?>? getBinding = null,
        IModbusHoldingRegisterAccessor? modbusRegisters = null)
    {
        Profile = profile.Normalize(1);
        _cameraProviders = cameraProviders;
        _resultWriter = resultWriter;
        _analysisResultWriter = analysisResultWriter;
        _labelWriter = labelWriter;
        _featureWriter = featureWriter;
        _visionTaskFactoryRegistry = visionTaskFactoryRegistry ?? throw new ArgumentNullException(nameof(visionTaskFactoryRegistry));
        _prepareStart = prepareStart;
        _getBinding = getBinding;
        _modbusRegisters = modbusRegisters ?? NullModbusHoldingRegisterAccessor.Instance;
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _pipelineSupportFactory = pipelineSupportFactory ?? throw new ArgumentNullException(nameof(pipelineSupportFactory));
        _framePresenter = new WpfFramePresenter(_uiDispatcher, ApplyPresentedFrame);
        _pipeline = new VideoPipeline(_cameraProviders);
        _sessionController = new PipelineSessionController(_pipeline, _resultWriter);

        _pipeline.FrameReady += OnFrameReady;
        _pipeline.StatsUpdated += OnStatsUpdated;
        _pipeline.Error += OnPipelineError;
        _pipeline.DeviceChanged += OnDeviceChanged;
        _pipeline.RunEnded += OnPipelineRunEnded;

        _tcnEngine = _pipelineSupportFactory.TryCreateTcnEngine();
        _tcnPredictionRecorder = _tcnEngine != null ? new TcnPredictionRecorder(_tcnEngine, _labelWriter) : null;
        _analysisEngine = analysisConfig.EnableOnlineAnalysis
            ? new AnalysisEngine(analysisConfig, modbusRegisters: _modbusRegisters)
            : null;
        if (_analysisEngine != null)
        {
            _analysisEngine.ResultReady += OnAnalysisResult;
        }

        ApplyFsmDefinitions(fsmDefinitions);
        var legacySink = _pipelineSupportFactory.BuildCompatibilityVisionResultSink(_resultWriter, _analysisEngine, _featureWriter, _tcnEngine);
        var ocrRelaySink = new OcrTextRelaySink(text => _uiDispatcher.Post(() => OcrText = text));
        _pipeline.SetVisionResultSink(new CompositeVisionResultSink(new IVisionResultSink[] { legacySink, ocrRelaySink }));
        _tcnEngine?.Start();

        statusText = Profile.Enabled ? "空闲" : "已禁用";
        sourceLabel = BuildSourceLabel(Profile.BuildOpenOptions());
        inferenceStatus = "等待启动";
        lastFrameInfo = "-";
        currentTimeText = "-";
        RunState = Profile.Enabled ? SessionRunState.Idle : SessionRunState.Disabled;

        XTrace.WriteLine("[CameraSession] Created session '{0}' ({1}), provider={2}, enabled={3}",
            Profile.Name, Profile.Id, Profile.ProviderId, Profile.Enabled);
    }

    public CameraProfile Profile { get; }
    public string Id => Profile.Id;
    public string Name => Profile.Name;
    public long CurrentPtsMs => Interlocked.Read(ref _currentPtsMs);
    public string CurrentSourceKey => _sessionController.CurrentSourceKey;
    public string CurrentRunUuid => _sessionController.CurrentRunUuid;
    public long CurrentRunStartedUtcMs => _sessionController.CurrentRunStartedUtcMs;
    public bool IsPrimaryTaskLoaded => _sessionController.IsPrimaryTaskLoaded;
    public bool IsAnalysisEnabled => _analysisEngine != null;
    public string ActivePrimaryTaskId => _sessionController.ActivePrimaryTaskId;
    public IReadOnlyList<string> ActiveSidecarTaskIds => _sessionController.ActiveSidecarTaskIds;
    public ITcnPredictionProvider? PredictionProvider => _tcnEngine;
    public ObservableCollection<FsmStepItem> FsmSteps { get; } = new();
    public IReadOnlyList<FsmStepSnapshot> FsmStepSnapshots => _fsmStepSnapshots;
    public SessionMetricsSnapshot MetricsSnapshot { get; private set; } = SessionMetricsSnapshot.Empty;
    public PipelinePerformanceSnapshot PerformanceSnapshot => MetricsSnapshot.Performance;
    public SessionControlState ControlState => BuildControlState();
    public SessionWorkspaceState WorkspaceState => BuildWorkspaceState();
    public SessionStatusSnapshot StatusSnapshot
    {
        get => _statusSnapshot;
        private set
        {
            if (_statusSnapshot.Equals(value))
            {
                return;
            }

            _statusSnapshot = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UiRunStateText));
            OnPropertyChanged(nameof(RunStateText));
            OnPropertyChanged(nameof(UiTransitionStatusText));
            OnPropertyChanged(nameof(TransitionStatusText));
            OnPropertyChanged(nameof(UiStatusBadgeText));
            OnPropertyChanged(nameof(StatusBadgeText));
        }
    }
    public string TabTitle => Profile.Name;
    public string CurrentVideoPath => _lastVideoPath;
    public bool HasVideoPath => !string.IsNullOrWhiteSpace(_lastVideoPath);
    public bool CanPlayVideo => ControlState.CanPlayVideo;
    public string CurrentVideoLabel => GetCurrentVideoLabel();
    public string UiControlSourceText => ControlState.IsVideoSource
        ? $"视频回放 / {ControlState.CurrentVideoLabel}"
        : $"实时采集 / {ControlState.SessionName}";
    public string UiRunStateText => GetRunStateText();
    public string UiControlHintText => ControlState.IsVideoSource
        ? "当前 session 使用视频文件作为输入源，推理、后处理和 SOP 链路保持不变。"
        : "当前 session 使用相机作为输入源，录像、推理和 SOP 状态彼此独立。";
    public string ControlSourceText => UiControlSourceText;
    public string RunStateText => UiRunStateText;
    public string ControlHintText => UiControlHintText;
    public string TargetFpsDisplay => Profile.TargetFps.ToString("F0", CultureInfo.InvariantCulture);
    public string SourceFpsDisplay => SourceFpsText;
    public string SourceDurationDisplay => SourceDurationText;
    public string PlaybackTimeDisplay => PlaybackTimeText;
    public string UiTransitionStatusText => GetTransitionStatusText();
    public string TransitionStatusText => UiTransitionStatusText;
    public string HeaderText => ControlState.IsRunning ? $"{ControlState.SessionName} 运行中" : ControlState.SessionName;
    public string HeaderSubtitle => ControlState.IsVideoSource
        ? $"Video / {ControlState.CurrentVideoLabel}"
        : ControlState.SourceLabel;
    public string UiStatusBadgeText => GetStatusBadgeText();
    public string StatusBadgeText => UiStatusBadgeText;
    public string SourceModeText => ControlState.IsVideoSource ? "视频" : "相机";
    public long ProductionTotalCount => ProductionOkCount + ProductionNgCount;
    public string ProductionYieldText => ProductionTotalCount > 0
        ? $"{ProductionOkCount * 100d / ProductionTotalCount:0.00}%"
        : "-";
    public double ProductionYieldPercent => ProductionTotalCount > 0
        ? ProductionOkCount * 100d / ProductionTotalCount
        : 0d;
    public string ProductionWorkDurationText => NormalizeProductionDuration(PlaybackTimeDisplay);
    public string ProductionEmployeeText => "未绑定";
    public string ProductionLastUpdateText => CurrentTimeText;
    public double CaptureStageAverageMs => PerformanceSnapshot.Capture.AverageMs;
    public double CaptureStageMaxMs => PerformanceSnapshot.Capture.MaxMs;
    public int CaptureStageSampleCount => PerformanceSnapshot.Capture.SampleCount;
    public double InferStageAverageMs => PerformanceSnapshot.Infer.AverageMs;
    public double InferStageMaxMs => PerformanceSnapshot.Infer.MaxMs;
    public int InferStageSampleCount => PerformanceSnapshot.Infer.SampleCount;
    public double ModelPreprocessAverageMs => PerformanceSnapshot.ModelPreprocess.AverageMs;
    public double ModelOrtRunAverageMs => PerformanceSnapshot.ModelOrtRun.AverageMs;
    public double ModelPostprocessAverageMs => PerformanceSnapshot.ModelPostprocess.AverageMs;
    public double ModelTotalAverageMs => PerformanceSnapshot.ModelTotal.AverageMs;
    public double AnnotateStageAverageMs => PerformanceSnapshot.Annotate.AverageMs;
    public double AnnotateStageMaxMs => PerformanceSnapshot.Annotate.MaxMs;
    public int AnnotateStageSampleCount => PerformanceSnapshot.Annotate.SampleCount;
    public double RenderPresentStageAverageMs => PerformanceSnapshot.RenderPresent.AverageMs;
    public double RenderPresentStageMaxMs => PerformanceSnapshot.RenderPresent.MaxMs;
    public int RenderPresentStageSampleCount => PerformanceSnapshot.RenderPresent.SampleCount;
    public double RecordEnqueueStageAverageMs => PerformanceSnapshot.RecordEnqueue.AverageMs;
    public double RecordEnqueueStageMaxMs => PerformanceSnapshot.RecordEnqueue.MaxMs;
    public int RecordEnqueueStageSampleCount => PerformanceSnapshot.RecordEnqueue.SampleCount;

    public event Action<SopFaultAlarmEvent>? SopFaultAlarmRaised;
    public event Action<SopFaultResetEvent>? SopFaultReset;

    [ObservableProperty]
    private ImageSource? videoFrame;

    [ObservableProperty]
    private string statusText = string.Empty;

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
    private bool isVideoSource;

    [ObservableProperty]
    private bool isPaused;

    [ObservableProperty]
    private SessionRunState runState;

    [ObservableProperty]
    private long productionOkCount;

    [ObservableProperty]
    private long productionNgCount;

    [ObservableProperty]
    private string sopOutcomeText = "等待";

    [ObservableProperty]
    private string sopOutcomeBackground = "#EDF2F7";

    [ObservableProperty]
    private string sopOutcomeForeground = "#607080";

    [ObservableProperty]
    private bool canResetSopFault;

    [ObservableProperty]
    private string ocrText = string.Empty;

    partial void OnIsRunningChanged(bool value)
    {
        RefreshStatusSnapshot();
        NotifyControlStateChanged();
        NotifyWorkspaceStateChanged();
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(StatusBadgeText));
        OnPropertyChanged(nameof(RunStateText));
        OnPropertyChanged(nameof(UiStatusBadgeText));
        OnPropertyChanged(nameof(UiRunStateText));
        StartCameraCommand.NotifyCanExecuteChanged();
        StopCameraCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsVideoSourceChanged(bool value)
    {
        RefreshStatusSnapshot();
        NotifyControlStateChanged();
        NotifyWorkspaceStateChanged();
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(CurrentVideoLabel));
        OnPropertyChanged(nameof(SourceModeText));
        OnPropertyChanged(nameof(UiControlSourceText));
        OnPropertyChanged(nameof(ControlSourceText));
        OnPropertyChanged(nameof(UiControlHintText));
        OnPropertyChanged(nameof(ControlHintText));
        OnPropertyChanged(nameof(UiRunStateText));
        OnPropertyChanged(nameof(RunStateText));
        OnPropertyChanged(nameof(UiStatusBadgeText));
        OnPropertyChanged(nameof(StatusBadgeText));
    }

    partial void OnIsPausedChanged(bool value)
    {
        RefreshStatusSnapshot();
        NotifyControlStateChanged();
        NotifyWorkspaceStateChanged();
        OnPropertyChanged(nameof(UiRunStateText));
        OnPropertyChanged(nameof(RunStateText));
        OnPropertyChanged(nameof(UiStatusBadgeText));
        OnPropertyChanged(nameof(StatusBadgeText));
    }

    partial void OnLastErrorChanged(string value)
    {
        RefreshStatusSnapshot();
        NotifyControlStateChanged();
        NotifyWorkspaceStateChanged();
        OnPropertyChanged(nameof(StatusBadgeText));
        OnPropertyChanged(nameof(UiStatusBadgeText));
    }

    partial void OnStatusTextChanged(string value)
    {
        RefreshStatusSnapshot();
        NotifyControlStateChanged();
        NotifyWorkspaceStateChanged();
        OnPropertyChanged(nameof(RunStateText));
        OnPropertyChanged(nameof(UiRunStateText));
    }

    partial void OnSourceLabelChanged(string value)
    {
        NotifyControlStateChanged();
        NotifyWorkspaceStateChanged();
    }
    partial void OnInferenceStatusChanged(string value) => NotifyWorkspaceStateChanged();
    partial void OnInferenceDeviceTextChanged(string value) => NotifyWorkspaceStateChanged();
    partial void OnLastFrameInfoChanged(string value) => NotifyWorkspaceStateChanged();

    partial void OnSourceFpsTextChanged(string value) => OnPropertyChanged(nameof(SourceFpsDisplay));
    partial void OnSourceDurationTextChanged(string value) => OnPropertyChanged(nameof(SourceDurationDisplay));
    partial void OnPlaybackTimeTextChanged(string value)
    {
        OnPropertyChanged(nameof(PlaybackTimeDisplay));
        OnPropertyChanged(nameof(ProductionWorkDurationText));
    }
    partial void OnCurrentTimeTextChanged(string value) => OnPropertyChanged(nameof(ProductionLastUpdateText));

    partial void OnProductionOkCountChanged(long value)
    {
        OnPropertyChanged(nameof(ProductionTotalCount));
        OnPropertyChanged(nameof(ProductionYieldText));
        OnPropertyChanged(nameof(ProductionYieldPercent));
    }

    partial void OnProductionNgCountChanged(long value)
    {
        OnPropertyChanged(nameof(ProductionTotalCount));
        OnPropertyChanged(nameof(ProductionYieldText));
        OnPropertyChanged(nameof(ProductionYieldPercent));
    }

    partial void OnLastTransitionOkChanged(bool? value)
    {
        RefreshStatusSnapshot();
        NotifyControlStateChanged();
        NotifyWorkspaceStateChanged();
        OnPropertyChanged(nameof(TransitionStatusText));
        OnPropertyChanged(nameof(UiTransitionStatusText));
    }

    partial void OnRunStateChanged(SessionRunState value)
    {
        RefreshStatusSnapshot();
        NotifyControlStateChanged();
        NotifyWorkspaceStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartCamera()
    {
        await StartAsync();
    }

    private bool CanStart() => ControlState.CanStartCamera;

    public async Task<bool> StartAsync()
    {
        if (IsRunning && IsVideoSource)
        {
            XTrace.WriteLine("[CameraSession] Switching from video to camera for '{0}'", Profile.Name);
            Stop();
        }
        else if (IsRunning)
        {
            return false;
        }

        XTrace.WriteLine("[CameraSession] Starting camera '{0}' ({1})", Profile.Name, Profile.Id);
        ResetSopRuntimeForNewRun();
        IsVideoSource = false;
        IsPaused = false;
        RunState = SessionRunState.Starting;
        StatusText = "Loading model...";
        InferenceStatus = "Initializing model...";
        SourceLabel = BuildSourceLabel(Profile.BuildOpenOptions(Profile.TargetFps));
        Interlocked.Exchange(ref _currentPtsMs, 0);

        // Phase 1: Load model on background thread (avoid UI freeze from InferenceSession creation)
        if (!await LoadModelAsync())
        {
            return false;
        }

        StartOrRefreshOcrSidecar();

        // Phase 2: Start camera (model already loaded, fast path)
        StatusText = "Starting camera...";
        InferenceStatus = "Opening camera...";
        var result = _sessionController.TryStartCamera(Profile, () => _prepareStart?.Invoke(this) ?? SessionStartPrecheckResult.Success);
        if (!result.Success)
        {
            var snapshot = result.ToStatusSnapshot();
            if (result.FailureKind == PipelineCameraStartFailureKind.StartFailed)
            {
                _pipeline.Stop();
                _sessionController.Stop(null, "failed");
                RunState = snapshot.RunState;
                StatusText = SessionStatusTextFormatter.GetRunStateText(snapshot);
                InferenceStatus = "Camera start failed";
            }
            else if (result.FailureKind == PipelineCameraStartFailureKind.NoModel)
            {
                RunState = snapshot.RunState;
                StatusText = SessionStatusTextFormatter.GetRunStateText(snapshot);
                InferenceStatus = "Please load a model before starting.";
            }
            else if (result.FailureKind == PipelineCameraStartFailureKind.Disabled)
            {
                RunState = snapshot.RunState;
                StatusText = SessionStatusTextFormatter.GetRunStateText(snapshot);
                InferenceStatus = "Enable the camera profile before starting.";
            }
            else
            {
                RunState = snapshot.RunState;
                StatusText = SessionStatusTextFormatter.GetRunStateText(snapshot);
                InferenceStatus = "Camera start was blocked by pre-check.";
            }

            LastError = result.Message ?? "Camera start failed.";
            return false;
        }

        IsRunning = true;
        RunState = SessionRunState.Running;
        StatusText = "Running";
        XTrace.WriteLine("[CameraSession] Camera '{0}' started successfully.", Profile.Name);
        return true;
    }

    private async Task<bool> LoadModelAsync()
    {
        var binding = _getBinding?.Invoke(this);
        if (binding == null)
        {
            // No binding means either: already loaded, no task configured,
            // or _getBinding not provided (fallback to sync prepareStart)
            return true;
        }

        try
        {
            // Create InferenceSession on background thread to avoid UI freeze
            var task = await Task.Run(() => binding.Registry.Create(binding.Definition, binding.Context));
            _sessionController.SetPrimaryTask(task, binding.Definition);
            WarmupModel();
            RefreshPrimaryWorkerStatusText();
            return true;
        }
        catch (Exception ex)
        {
            RunState = SessionRunState.Error;
            StatusText = "Model load failed";
            InferenceStatus = "Model initialization error";
            LastError = $"Failed to load model: {ex.Message}";
            CameraDiagnostics.Error("session", "Async model load failed.", ex);
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void StopCamera()
    {
        Stop();
    }

    private bool CanStop() => ControlState.CanStopCamera;

    public void ApplyPrimaryVisionTask(
        VisionTaskDefinition definition,
        VisionTaskFactoryRegistry registry,
        VisionTaskCreationContext context)
    {
        ApplyPrimaryVisionTask(PrimaryVisionTaskBinding.ForTask(definition, registry, context));
    }

    void IVisionTaskBindingTarget.ApplyPrimaryVisionTask(PrimaryVisionTaskBinding binding)
    {
        ApplyPrimaryVisionTask(binding, sharedTask: null);
    }

    public void ApplyPrimaryVisionTask(PrimaryVisionTaskBinding binding, IVisionTask? sharedTask = null)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (binding.ModelBindingPlan != null)
        {
            _pipeline.UpdateDetectionDrawOptions(
                binding.ModelBindingPlan.BoxColor,
                binding.ModelBindingPlan.BoxColors,
                binding.ModelBindingPlan.BoxThickness,
                binding.ModelBindingPlan.LabelFontScale);
        }

        if (sharedTask != null)
        {
            _sessionController.SetPrimaryTask(sharedTask, binding.Definition);
        }
        else
        {
            _sessionController.ApplyPrimaryTask(binding.Definition, binding.Registry, binding.Context);
        }

        StatusText = Profile.Enabled ? "空闲" : "已禁用";
        InferenceStatus = binding.ModelBindingPlan != null
            ? GetModelBackedTaskStatusText(binding.Definition)
            : GetTaskStatusText(binding.Definition);
        InferenceDeviceText = sharedTask?.ActiveDeviceLabel ?? GetTaskDeviceText(binding.Definition, binding.Context);
        RunState = Profile.Enabled ? SessionRunState.Idle : SessionRunState.Disabled;
        WarmupModel();
        RefreshPrimaryWorkerStatusText();
        NotifyWorkspaceStateChanged();
        NotifyControlStateChanged();
    }

    public void ApplySidecarVisionTasks(
        IEnumerable<VisionTaskDefinition> definitions,
        VisionTaskFactoryRegistry registry,
        VisionTaskCreationContext context)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(registry);

        _sessionController.ApplySidecarTasks(definitions, registry, context);
        NotifyWorkspaceStateChanged();
    }

    public void ClearSidecarVisionTasks()
    {
        _sessionController.ClearSidecarTasks();
        NotifyWorkspaceStateChanged();
    }

    public void StartOrRefreshOcrSidecar()
    {
        if (!Profile.OcrEnabled)
        {
            ClearSidecarVisionTasks();
            OcrText = string.Empty;
            return;
        }

        var manifestPath = Path.Combine(AppContext.BaseDirectory, "DL", "en_PP-OCRv4_rec", "model.json");
        if (!TryReadOcrManifest(manifestPath, out var modelPath, out var dictPath, out var fixedWidth, out var fixedHeight))
        {
            ClearSidecarVisionTasks();
            OcrText = string.Empty;
            return;
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["modelPath"] = modelPath,
            ["dictPath"] = dictPath,
            ["deviceKind"] = InferenceDeviceKind.GpuCuda.ToString(),
            ["roiX"] = Profile.OcrRoiX.ToString(CultureInfo.InvariantCulture),
            ["roiY"] = Profile.OcrRoiY.ToString(CultureInfo.InvariantCulture),
            ["roiWidth"] = Profile.OcrRoiWidth.ToString(CultureInfo.InvariantCulture),
            ["roiHeight"] = Profile.OcrRoiHeight.ToString(CultureInfo.InvariantCulture),
            ["inputWidth"] = fixedWidth.ToString(CultureInfo.InvariantCulture),
            ["inputHeight"] = fixedHeight.ToString(CultureInfo.InvariantCulture),
            ["fixedWidth"] = fixedWidth.ToString(CultureInfo.InvariantCulture),
            ["fixedHeight"] = fixedHeight.ToString(CultureInfo.InvariantCulture)
        };

        var definition = new VisionTaskDefinition
        {
            Id = $"ocr-sidecar-{Id}",
            DisplayName = "PP-OCRv4 英文识别",
            TaskKind = VisionTaskKind.OcrText,
            RuntimeKind = VisionRuntimeKind.OcrRuntime,
            Metadata = new ReadOnlyDictionary<string, string>(metadata)
        };

        var context = new VisionTaskCreationContext(
            InferenceDeviceKind.GpuCuda,
            0.25f,
            0.45f);

        ApplySidecarVisionTasks(new[] { definition }, _visionTaskFactoryRegistry, context);
    }

    private static bool TryReadOcrManifest(
        string manifestPath,
        out string modelPath,
        out string dictPath,
        out int inputWidth,
        out int inputHeight)
    {
        modelPath = string.Empty;
        dictPath = string.Empty;
        inputWidth = 0;
        inputHeight = 0;
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var bundleDirectory = Path.GetDirectoryName(manifestPath) ?? AppContext.BaseDirectory;
            modelPath = TryReadManifestString(root, "modelFile", out var modelFile)
                ? ResolveManifestFile(bundleDirectory, modelFile)
                : TryReadManifestString(root, "modelPath", out var modelPathValue)
                    ? ResolveManifestFile(bundleDirectory, modelPathValue)
                    : string.Empty;
            dictPath = TryReadManifestString(root, "dictFile", out var dictFile)
                ? ResolveManifestFile(bundleDirectory, dictFile)
                : TryReadManifestString(root, "dictPath", out var dictPathValue)
                    ? ResolveManifestFile(bundleDirectory, dictPathValue)
                    : string.Empty;
            inputWidth = root.TryGetProperty("inputWidth", out var widthElement) &&
                         widthElement.TryGetInt32(out var width)
                ? width
                : 0;
            inputHeight = root.TryGetProperty("inputHeight", out var heightElement) &&
                          heightElement.TryGetInt32(out var height)
                ? height
                : 0;

            return !string.IsNullOrWhiteSpace(modelPath) &&
                   File.Exists(modelPath) &&
                   !string.IsNullOrWhiteSpace(dictPath) &&
                   File.Exists(dictPath) &&
                   inputWidth > 0 &&
                   inputHeight > 0;
        }
        catch
        {
            modelPath = string.Empty;
            dictPath = string.Empty;
            inputWidth = 0;
            inputHeight = 0;
            return false;
        }
    }

    private static string ResolveManifestFile(string bundleDirectory, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Path.IsPathRooted(value)
            ? value
            : Path.GetFullPath(Path.Combine(bundleDirectory, value));
    }

    private static bool TryReadManifestString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    public void ApplyFsmDefinitions(IReadOnlyList<FsmStepDefinition> definitions)
    {
        _fsmDefinitions.Clear();
        _fsmStepSnapshots.Clear();
        if (definitions != null)
        {
            _fsmDefinitions.AddRange(definitions.OrderBy(item => item.Step));
        }

        _analysisEngine?.UpdateFsmDefinitions(_fsmDefinitions);

        foreach (var definition in _fsmDefinitions)
        {
            _fsmStepSnapshots.Add(FsmStepSnapshot.FromDefinition(definition));
        }

        SyncFsmItemsFromSnapshots();
        NotifyWorkspaceStateChanged();
    }

    public bool OpenVideo(string path, double targetFps, bool useSourcePtsForVideo)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (!File.Exists(path))
        {
            LastError = $"Video file not found: {path}";
            RunState = SessionRunState.Error;
            StatusText = "Error";
            InferenceStatus = "Video file not found";
            return false;
        }

        if (!IsPrimaryTaskLoaded)
        {
            LastError = "Please load a model or task before starting video playback.";
            RunState = SessionRunState.NoModel;
            StatusText = "No Model";
            InferenceStatus = "Please load a model or task before starting video playback.";
            InferenceDeviceText = "-";
            return false;
        }

        if (IsRunning)
        {
            Stop();
        }

        _lastVideoPath = path;
        ResetSopRuntimeForNewRun();
        IsVideoSource = true;
        IsPaused = false;
        RunState = SessionRunState.Starting;
        StatusText = "Starting...";
        InferenceStatus = "Opening video...";
        SourceLabel = Path.GetFileName(path);
        Interlocked.Exchange(ref _currentPtsMs, 0);
        OnPropertyChanged(nameof(CurrentVideoLabel));
        OnPropertyChanged(nameof(HeaderSubtitle));
        NotifyControlStateChanged();
        NotifyWorkspaceStateChanged();

        var startInfo = _sessionController.StartVideo(path, targetFps, useSourcePtsForVideo);
        SourceLabel = startInfo.SourceLabel;
        IsRunning = true;
        RunState = SessionRunState.Running;
        StatusText = "Running";
        return true;
    }

    public bool PlayVideo(double targetFps, bool useSourcePtsForVideo)
    {
        if (IsPaused)
        {
            _pipeline.Resume();
            IsPaused = false;
            RunState = SessionRunState.Running;
            StatusText = "Running";
            InferenceStatus = $"Playback resumed @ {PlaybackTimeDisplay}";
            return true;
        }

        if (string.IsNullOrWhiteSpace(_lastVideoPath))
        {
            return false;
        }

        return OpenVideo(_lastVideoPath, targetFps, useSourcePtsForVideo);
    }

    public void PauseVideo()
    {
        if (!IsVideoSource || !IsRunning || IsPaused)
        {
            return;
        }

        _pipeline.Pause();
        IsPaused = true;
        RunState = SessionRunState.Paused;
        StatusText = "Paused";
        InferenceStatus = $"Playback paused @ {PlaybackTimeDisplay}";
    }

    public void UpdatePlaybackTargetFps(double targetFps)
    {
        _pipeline.UpdateTargetFps(targetFps);
    }

    public void Stop(string status = "stopped")
    {
        XTrace.WriteLine("[CameraSession] Stopping '{0}', reason={1}", Profile.Name, status);
        _sessionController.Stop(_tcnPredictionRecorder, status);
        IsRunning = false;
        IsPaused = false;
        RunState = Profile.Enabled || IsVideoSource ? SessionRunState.Stopped : SessionRunState.Disabled;
        StatusText = Profile.Enabled || IsVideoSource ? "Stopped" : "Disabled";
        InferenceStatus = IsVideoSource ? "Playback stopped" : "Camera stopped";
        InferenceDeviceText = "-";
    }

    public void RequestRecordingRotate(string? reason = null)
    {
        _sessionController.RequestRecordingRotate(reason);
    }

    public void Dispose()
    {
        XTrace.WriteLine("[CameraSession] Disposing '{0}'", Profile.Name);
        _sessionController.Stop(_tcnPredictionRecorder, "stopped");
        _sessionController.ClearTasks();
        if (_analysisEngine != null)
        {
            _analysisEngine.ResultReady -= OnAnalysisResult;
        }

        _pipeline.FrameReady -= OnFrameReady;
        _pipeline.StatsUpdated -= OnStatsUpdated;
        _pipeline.Error -= OnPipelineError;
        _pipeline.DeviceChanged -= OnDeviceChanged;
        _pipeline.RunEnded -= OnPipelineRunEnded;
        _pipeline.Dispose();
        _tcnPredictionRecorder?.Dispose();
        _tcnEngine?.Dispose();
    }

    private void OnFrameReady(RenderPacket packet)
    {
        _framePresenter.Present(packet);
    }

    private void OnStatsUpdated(PipelineStats stats)
    {
        _uiDispatcher.Post(() =>
        {
            var snapshot = SessionMetricsFormatter.FromPipelineStats(stats, DateTimeOffset.Now);
            MetricsSnapshot = snapshot;
            ApplyMetricsSnapshot(snapshot);
        });
    }

    private void ApplyMetricsSnapshot(SessionMetricsSnapshot snapshot)
    {
        Interlocked.Exchange(ref _currentPtsMs, snapshot.CurrentPtsMs);
        CaptureFps = snapshot.CaptureFps;
        InferFps = snapshot.InferFps;
        RenderFps = snapshot.RenderFps;
        SourceFpsText = snapshot.SourceFpsText;
        SourceDurationText = snapshot.SourceDurationText;
        PlaybackTimeText = snapshot.PlaybackTimeText;
        CurrentTimeText = snapshot.CurrentTimeText;
        FrameQueueSize = snapshot.FrameQueueSize;
        RenderQueueSize = snapshot.RenderQueueSize;
        DroppedByPts = snapshot.DroppedByPts;
        DroppedByCaptureQueue = snapshot.DroppedByCaptureQueue;
        DroppedByInferDrain = snapshot.DroppedByInferDrain;
        DroppedByRenderQueue = snapshot.DroppedByRenderQueue;
        DroppedByRenderDrain = snapshot.DroppedByRenderDrain;
        InferenceStatus = snapshot.CaptureSummaryText;
        OnPropertyChanged(nameof(MetricsSnapshot));
        NotifyWorkspaceStateChanged();
        RaisePerformancePropertyChanged();
    }

    private void OnPipelineError(string message)
    {
        _uiDispatcher.Post(() =>
        {
            if (message.StartsWith("FFmpeg decode failed, fallback to OpenCV", StringComparison.OrdinalIgnoreCase))
            {
                LastError = string.Empty;
                InferenceStatus = "FFmpeg fallback to OpenCV";
                StatusText = "Running (OpenCV)";
            }
            else if (message.Contains("CPU", StringComparison.OrdinalIgnoreCase))
            {
                LastError = message;
                InferenceStatus = "CPU fallback";
                StatusText = "Running (CPU)";
                InferenceDeviceText = "OnnxRuntime / CPU";
            }
            else
            {
                LastError = message;
                RunState = SessionRunState.Error;
                StatusText = "Error";
            }
        });
    }

    private void OnPipelineRunEnded(PipelineRunEnded ended)
    {
        _uiDispatcher.Post(() =>
        {
            var currentRunUuid = _sessionController.CurrentRunUuid;
            if (!IsRunning ||
                string.IsNullOrWhiteSpace(currentRunUuid) ||
                !string.Equals(currentRunUuid, ended.RunUuid, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var status = ended.Reason switch
            {
                PipelineRunEndReason.SourceEnded => "completed",
                PipelineRunEndReason.SourceError => "failed",
                _ => "stopped"
            };

            _tcnPredictionRecorder?.Flush();
            _sessionController.Stop(_tcnPredictionRecorder, status);
            IsRunning = false;
            IsPaused = false;
            InferenceDeviceText = "-";

            if (ended.Reason == PipelineRunEndReason.SourceEnded)
            {
                RunState = SessionRunState.Completed;
                StatusText = "Completed";
                InferenceStatus = IsVideoSource ? "Playback finished" : "Capture finished";
            }
            else if (ended.Reason == PipelineRunEndReason.SourceError)
            {
                RunState = SessionRunState.Stopped;
                StatusText = "Stopped";
                InferenceStatus = IsVideoSource ? "Playback ended unexpectedly" : "Camera ended unexpectedly";
            }
            else
            {
                RunState = SessionRunState.Stopped;
                StatusText = "Stopped";
                InferenceStatus = IsVideoSource ? "Playback stopped" : "Camera stopped";
            }
        });
    }

    private void OnDeviceChanged(string label)
    {
        _uiDispatcher.Post(() =>
        {
            InferenceDeviceText = label;
            RefreshPrimaryWorkerStatusText();
        });
    }

    private void OnAnalysisResult(AnalysisResult result)
    {
        _analysisResultWriter.TryEnqueue(result);

        _uiDispatcher.Post(() =>
        {
            if (!string.IsNullOrWhiteSpace(result.NgReason))
            {
                LastTransitionOk = false;
                LastError = result.NgReason;
                SetSopOutcomeNg();
                if (!_sopFaultActive)
                {
                    ProductionNgCount++;
                    SopFaultAlarmRaised?.Invoke(new SopFaultAlarmEvent(
                        this,
                        Guid.NewGuid().ToString("N"),
                        result.RunUuid,
                        result.SourceKey,
                        result.Step,
                        result.NgReason,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                }

                _sopFaultActive = true;
            }

            if (result.IsTransition && result.TransitionOk.HasValue)
            {
                LastTransitionOk = result.TransitionOk.Value;
                if (!result.TransitionOk.Value)
                {
                    ProductionNgCount++;
                }
            }

            if (result.IsSopCycleReset)
            {
                LastTransitionOk = true;
                ProductionOkCount++;
            }

            ApplyAnalysisStep(
                result.Step.HasValue ? result.Step + 1 : null,
                result.IsReset,
                !string.IsNullOrWhiteSpace(result.NgReason) ? false : result.TransitionOk,
                !string.IsNullOrWhiteSpace(result.NgReason));
        });
    }

    private void ApplyAnalysisStep(int? step, bool isReset, bool? transitionOk, bool isNg = false)
    {
        if (_fsmStepSnapshots.Count == 0)
        {
            return;
        }

        // isReset 时（最后一步完成后重置）：重置状态值
        if (isReset)
        {
            ResetFsmSnapshots();
            // transitionOk 和 step 由 AnalysisEngine 传递，不要覆盖
        }

        if (!step.HasValue)
        {
            if (!isNg)
            {
                ClearActiveFsmSnapshots();
            }

            SyncFsmItemsFromSnapshots();
            NotifyWorkspaceStateChanged();
            return;
        }

        // 当 step 有值时，不要清除 active 状态，直接更新步骤显示
        if (isNg)
        {
            ApplyFsmSnapshotNgStep(step.Value);
        }
        else
        {
            ApplyFsmSnapshotStep(step.Value, transitionOk);
        }

        SyncFsmItemsFromSnapshots();
        NotifyWorkspaceStateChanged();
    }

    private static bool IsSopCycleReset(AnalysisResult result)
    {
        if (result.IsSopCycleReset)
        {
            return true;
        }

        return (string.Equals(result.StrategyName, AnalysisStrategyNames.Sop1, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(result.DebugNote, "sop1_cycle_reset", StringComparison.OrdinalIgnoreCase)) ||
               (string.Equals(result.StrategyName, AnalysisStrategyNames.Sop2, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(result.DebugNote, "sop2_cycle_reset", StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyPresentedFrame(ImageSource frame, string? frameInfo)
    {
        VideoFrame = frame;
        if (!string.IsNullOrWhiteSpace(frameInfo))
        {
            LastFrameInfo = frameInfo;
        }
    }

    private void WarmupModel()
    {
        Task.Run(() =>
        {
            _pipeline.Warmup();
            _uiDispatcher.Post(RefreshPrimaryWorkerStatusText);
        });
    }

    private string GetRunStateText()
    {
        return SessionStatusTextFormatter.GetRunStateText(StatusSnapshot);
    }

    private string GetTransitionStatusText()
    {
        return SessionStatusTextFormatter.GetTransitionStatusText(StatusSnapshot);
    }

    private string GetStatusBadgeText()
    {
        return SessionStatusTextFormatter.GetStatusBadgeText(StatusSnapshot);
    }

    private void RefreshStatusSnapshot()
    {
        var transitionState = LastTransitionOk switch
        {
            true => SessionTransitionState.Normal,
            false => SessionTransitionState.Abnormal,
            _ => SessionTransitionState.Unknown
        };

        StatusSnapshot = new SessionStatusSnapshot(
            RunState,
            IsVideoSource,
            transitionState,
            LastError);
    }

    private SessionWorkspaceState BuildWorkspaceState()
    {
        return new SessionWorkspaceState(
            StatusSnapshot,
            MetricsSnapshot,
            Name,
            GetCurrentVideoLabel(),
            SourceLabel,
            InferenceStatus,
            InferenceDeviceText,
            LastFrameInfo,
            LastError,
            TargetFpsDisplay,
            !string.IsNullOrWhiteSpace(_lastVideoPath),
            _fsmStepSnapshots.ToArray());
    }

    private SessionControlState BuildControlState()
    {
        return new SessionControlState(
            StatusSnapshot,
            Name,
            SourceLabel,
            GetCurrentVideoLabel(),
            !string.IsNullOrWhiteSpace(_lastVideoPath),
            Profile.Enabled);
    }

    private string GetCurrentVideoLabel()
    {
        return string.IsNullOrWhiteSpace(_lastVideoPath)
            ? "-"
            : Path.GetFileName(_lastVideoPath);
    }

    private static string NormalizeProductionDuration(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || source == "-")
        {
            return "-";
        }

        var normalized = source.Trim();
        var dot = normalized.IndexOf('.');
        if (dot >= 0)
        {
            normalized = normalized[..dot];
        }

        var parts = normalized.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            return $"00:{parts[0]}:{parts[1]}";
        }

        return parts.Length == 3 ? normalized : "-";
    }

    private void ResetProductionStats()
    {
        ProductionOkCount = 0;
        ProductionNgCount = 0;
    }

    private void NotifyWorkspaceStateChanged()
    {
        OnPropertyChanged(nameof(WorkspaceState));
        OnPropertyChanged(nameof(FsmStepSnapshots));
    }

    private void ResetSopRuntimeForNewRun()
    {
        _analysisEngine?.ResetAnalysis();
        LastError = string.Empty;
        LastTransitionOk = null;
        SetSopOutcomeWaiting();
        ResetFsmSnapshots();
        SyncFsmItemsFromSnapshots();
        ResetProductionStats();
        NotifyWorkspaceStateChanged();
    }

    private void NotifyControlStateChanged()
    {
        OnPropertyChanged(nameof(ControlState));
        OnPropertyChanged(nameof(HasVideoPath));
        OnPropertyChanged(nameof(CanPlayVideo));
        OnPropertyChanged(nameof(CurrentVideoLabel));
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(UiControlSourceText));
        OnPropertyChanged(nameof(ControlSourceText));
        OnPropertyChanged(nameof(UiControlHintText));
        OnPropertyChanged(nameof(ControlHintText));
        OnPropertyChanged(nameof(SourceModeText));
    }

    private void SyncFsmItemsFromSnapshots()
    {
        var timelineOriginUtc = FsmStepItem.ResolveTimelineOrigin(_fsmStepSnapshots);
        FsmSteps.Clear();
        foreach (var snapshot in _fsmStepSnapshots)
        {
            FsmSteps.Add(FsmStepItem.FromSnapshot(snapshot, timelineOriginUtc));
        }
    }

    private void ResetFsmSnapshots()
    {
        for (var i = 0; i < _fsmStepSnapshots.Count; i++)
        {
            var current = _fsmStepSnapshots[i];
            _fsmStepSnapshots[i] = current with
            {
                Status = FsmStepStatus.Waiting,
                StartTimeUtc = null,
                EndTimeUtc = null,
                Duration = null,
                IsNg = false
            };
        }
    }

    private void ClearActiveFsmSnapshots()
    {
        for (var i = 0; i < _fsmStepSnapshots.Count; i++)
        {
            var current = _fsmStepSnapshots[i];
            if (current.Status == FsmStepStatus.InProgress)
            {
                _fsmStepSnapshots[i] = current with { Status = FsmStepStatus.Waiting };
            }
        }
    }

    private void ApplyFsmSnapshotStep(int step, bool? transitionOk)
    {
        // 追踪当前正在进行的步骤
        var inProgressStep = step - 1;

        for (var i = 0; i < _fsmStepSnapshots.Count; i++)
        {
            var current = _fsmStepSnapshots[i];

            // 完成判定：current.Step == inProgressStep && transitionOk == true
            // 注意：只有当 current.Step == inProgressStep（不是 <）时才判定为完成
            if (current.Step == inProgressStep && transitionOk == true)
            {
                // inProgressStep 步完成：标记为 Done
                var endTime = current.EndTimeUtc ?? DateTimeOffset.UtcNow;
                var duration = current.StartTimeUtc.HasValue ? endTime - current.StartTimeUtc.Value : current.Duration;
                _fsmStepSnapshots[i] = current with
                {
                    Status = FsmStepStatus.Done,
                    StartTimeUtc = current.StartTimeUtc ?? DateTimeOffset.UtcNow,
                    EndTimeUtc = endTime,
                    Duration = duration
                };
            }
            else if (current.Step == step)
            {
                // 进行中判定：current.Step == step（第 step 步处于进行中）
                _fsmStepSnapshots[i] = current with
                {
                    Status = FsmStepStatus.InProgress,
                    StartTimeUtc = current.StartTimeUtc ?? DateTimeOffset.UtcNow,
                    IsNg = transitionOk == false
                };
            }
            // 其他步骤保持原状态不变
        }

        if (!_sopFaultActive &&
            transitionOk != false &&
            _fsmStepSnapshots.Count > 0 &&
            step >= _fsmStepSnapshots.Max(item => item.Step))
        {
            SetSopOutcomeOk();
        }
    }

    private void ApplyFsmSnapshotNgStep(int step)
    {
        var found = _fsmStepSnapshots.Any(item => item.Step == step);
        if (!found)
        {
            return;
        }

        for (var i = 0; i < _fsmStepSnapshots.Count; i++)
        {
            var current = _fsmStepSnapshots[i];
            if (current.Step < step)
            {
                var endTime = current.EndTimeUtc ?? DateTimeOffset.UtcNow;
                var duration = current.StartTimeUtc.HasValue ? endTime - current.StartTimeUtc.Value : current.Duration;
                _fsmStepSnapshots[i] = current with
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
                _fsmStepSnapshots[i] = current with
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
                _fsmStepSnapshots[i] = current with
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
        ResetSopFault(SopFaultResetContext.ManualButton);
    }

    public void ResetSopFault(SopFaultResetContext? context)
    {
        if (!_sopFaultActive && !CanResetSopFault)
        {
            return;
        }

        var resetEvent = new SopFaultResetEvent(
            this,
            Guid.NewGuid().ToString("N"),
            CurrentRunUuid,
            CurrentSourceKey,
            _fsmStepSnapshots.FirstOrDefault(item => item.IsNg)?.Step,
            LastError,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            context ?? SopFaultResetContext.ManualButton);

        _sopFaultActive = false;
        _analysisEngine?.ResetAnalysis();
        LastError = string.Empty;
        LastTransitionOk = null;
        SetSopOutcomeWaiting();
        ResetFsmSnapshots();
        SyncFsmItemsFromSnapshots();
        NotifyWorkspaceStateChanged();
        SopFaultReset?.Invoke(resetEvent);
    }

    private void SetSopOutcomeWaiting()
    {
        _sopFaultActive = false;
        SopOutcomeText = "等待";
        SopOutcomeBackground = "#EDF2F7";
        SopOutcomeForeground = "#607080";
        CanResetSopFault = false;
        ResetSopFaultCommand.NotifyCanExecuteChanged();
    }

    private void SetSopOutcomeOk()
    {
        SopOutcomeText = "OK";
        SopOutcomeBackground = "#2E7D4F";
        SopOutcomeForeground = "White";
        CanResetSopFault = false;
        ResetSopFaultCommand.NotifyCanExecuteChanged();
    }

    private void SetSopOutcomeNg()
    {
        SopOutcomeText = "NG";
        SopOutcomeBackground = "#A83228";
        SopOutcomeForeground = "White";
        CanResetSopFault = true;
        ResetSopFaultCommand.NotifyCanExecuteChanged();
    }

    private void RaisePerformancePropertyChanged()
    {
        OnPropertyChanged(nameof(PerformanceSnapshot));
        OnPropertyChanged(nameof(CaptureStageAverageMs));
        OnPropertyChanged(nameof(CaptureStageMaxMs));
        OnPropertyChanged(nameof(CaptureStageSampleCount));
        OnPropertyChanged(nameof(InferStageAverageMs));
        OnPropertyChanged(nameof(InferStageMaxMs));
        OnPropertyChanged(nameof(InferStageSampleCount));
        OnPropertyChanged(nameof(ModelPreprocessAverageMs));
        OnPropertyChanged(nameof(ModelOrtRunAverageMs));
        OnPropertyChanged(nameof(ModelPostprocessAverageMs));
        OnPropertyChanged(nameof(ModelTotalAverageMs));
        OnPropertyChanged(nameof(AnnotateStageAverageMs));
        OnPropertyChanged(nameof(AnnotateStageMaxMs));
        OnPropertyChanged(nameof(AnnotateStageSampleCount));
        OnPropertyChanged(nameof(RenderPresentStageAverageMs));
        OnPropertyChanged(nameof(RenderPresentStageMaxMs));
        OnPropertyChanged(nameof(RenderPresentStageSampleCount));
        OnPropertyChanged(nameof(RecordEnqueueStageAverageMs));
        OnPropertyChanged(nameof(RecordEnqueueStageMaxMs));
        OnPropertyChanged(nameof(RecordEnqueueStageSampleCount));
    }

    private string BuildSourceLabel(CameraOpenOptions options)
    {
        return _cameraProviders.GetDisplayName(options.ProviderId);
    }

    private static string GetTaskStatusText(VisionTaskDefinition definition)
    {
        return definition.TaskKind switch
        {
            VisionTaskKind.SequenceBands => $"{definition.DisplayName} / Sequence Task",
            VisionTaskKind.HandLandmarks => $"{definition.DisplayName} / Hand Landmarks Task",
            VisionTaskKind.OcrText => $"{definition.DisplayName} / OCR Task",
            VisionTaskKind.PresenceClassification => $"{definition.DisplayName} / 产品有无分类 Task",
            _ => $"{definition.DisplayName} / Detection Task"
        };
    }

    private static string GetModelBackedTaskStatusText(VisionTaskDefinition definition)
    {
        return definition.TaskKind switch
        {
            VisionTaskKind.SequenceBands => $"{definition.DisplayName} / Sequence ONNX",
            VisionTaskKind.PresenceClassification => $"{definition.DisplayName} / 产品有无分类 ONNX",
            _ => $"{definition.DisplayName} / Detection ONNX"
        };
    }

    private static string GetTaskDeviceText(VisionTaskDefinition definition, VisionTaskCreationContext context)
    {
        return definition.RuntimeKind switch
        {
            VisionRuntimeKind.MediaPipe => "MediaPipe / Runtime",
            VisionRuntimeKind.OcrRuntime => "OCR / Runtime",
            _ => context.OnnxDeviceKind == InferenceDeviceKind.Cpu
                ? "OnnxRuntime / CPU"
                : "OnnxRuntime / GPU (Auto)"
        };
    }

    private void RefreshPrimaryWorkerStatusText()
    {
        var status = _sessionController.GetPrimaryWorkerStatus();
        if (status == null)
        {
            return;
        }

        var stateText = status.State.ToString();
        InferenceDeviceText = !string.IsNullOrWhiteSpace(status.RuntimeLabel)
            ? $"{status.RuntimeLabel} / {stateText}"
            : $"Worker / {stateText}";

        if (status.State == VisionWorkerState.Starting)
        {
            InferenceStatus = "Worker starting...";
            return;
        }

        if (status.State == VisionWorkerState.Ready)
        {
            if (InferenceStatus.Contains("Worker", StringComparison.OrdinalIgnoreCase))
            {
                InferenceStatus = "Worker ready";
            }

            return;
        }

        if (status.State == VisionWorkerState.Faulted || status.State == VisionWorkerState.Degraded)
        {
            InferenceStatus = !string.IsNullOrWhiteSpace(status.LastError)
                ? $"Worker {stateText}: {status.LastError}"
                : $"Worker {stateText}";
        }
    }
}
