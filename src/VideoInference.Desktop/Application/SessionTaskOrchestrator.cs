using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using OpenCvSharp;

namespace VideoInferenceDemo;

public sealed class SessionTaskOrchestrator
{
    private readonly CameraProviderRegistry _cameraProviders;
    private readonly SqliteResultWriter _resultWriter;
    private readonly SqliteAnalysisResultWriter _analysisResultWriter;
    private readonly TcnLabelWriter _labelWriter;
    private readonly TcnFeatureWriter? _featureWriter;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IDesktopPipelineSupportFactory _pipelineSupportFactory;
    private readonly VisionTaskFactoryRegistry _visionTaskFactoryRegistry;
    private readonly IModbusHoldingRegisterAccessor _modbusRegisters;

    public SessionTaskOrchestrator(
        CameraProviderRegistry cameraProviders,
        SqliteResultWriter resultWriter,
        SqliteAnalysisResultWriter analysisResultWriter,
        TcnLabelWriter labelWriter,
        TcnFeatureWriter? featureWriter,
        IUiDispatcher uiDispatcher,
        IDesktopPipelineSupportFactory pipelineSupportFactory,
        VisionTaskFactoryRegistry visionTaskFactoryRegistry,
        IModbusHoldingRegisterAccessor? modbusRegisters = null)
    {
        _cameraProviders = cameraProviders ?? throw new ArgumentNullException(nameof(cameraProviders));
        _resultWriter = resultWriter ?? throw new ArgumentNullException(nameof(resultWriter));
        _analysisResultWriter = analysisResultWriter ?? throw new ArgumentNullException(nameof(analysisResultWriter));
        _labelWriter = labelWriter ?? throw new ArgumentNullException(nameof(labelWriter));
        _featureWriter = featureWriter;
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _pipelineSupportFactory = pipelineSupportFactory ?? throw new ArgumentNullException(nameof(pipelineSupportFactory));
        _visionTaskFactoryRegistry = visionTaskFactoryRegistry ?? throw new ArgumentNullException(nameof(visionTaskFactoryRegistry));
        _modbusRegisters = modbusRegisters ?? NullModbusHoldingRegisterAccessor.Instance;
    }

    public PrimaryVisionTaskBinding CreatePrimaryTaskBinding(
        VisionTaskDefinition selectedPrimaryTask,
        VisionTaskCreationContext context)
    {
        ArgumentNullException.ThrowIfNull(selectedPrimaryTask);
        ArgumentNullException.ThrowIfNull(context);

        return PrimaryVisionTaskBinding.ForTask(
            selectedPrimaryTask,
            _visionTaskFactoryRegistry,
            context);
    }

    public void ApplyPrimaryTaskBindingToAllSessions(
        PrimaryVisionTaskBinding binding,
        IEnumerable<CameraSessionViewModel> sessions)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(sessions);

        var enabledSessions = sessions.Where(s => s.Profile.Enabled).ToList();
        if (enabledSessions.Count == 0)
        {
            return;
        }

        if (IsSharedRuntime(binding.Definition))
        {
            var sharedTask = TryCreatePrimaryTask(binding);
            if (sharedTask == null)
            {
                return;
            }

            var sharedHost = new SharedVisionTaskHost(sharedTask);
            foreach (var session in enabledSessions)
            {
                session.ApplyPrimaryVisionTask(binding, sharedHost.Acquire());
            }
            return;
        }

        foreach (var session in enabledSessions)
        {
            session.ApplyPrimaryVisionTask(binding, sharedTask: null);
        }
    }

    public IVisionTask? TryCreatePrimaryTask(PrimaryVisionTaskBinding binding)
    {
        if (binding?.Definition == null || binding.Registry == null)
        {
            return null;
        }

        try
        {
            return binding.Registry.Create(binding.Definition, binding.Context);
        }
        catch
        {
            return null;
        }
    }

    public SessionRebuildResult RehydrateSessions(
        ObservableCollection<CameraSessionViewModel> sessions,
        CameraSettings cameraSettings,
        AnalysisConfig analysisConfig,
        IReadOnlyList<SopProfile> sopProfiles,
        PrimaryVisionTaskBinding? activePrimaryBinding,
        VisionTaskDefinition? selectedPrimaryTask,
        VisionTaskCreationContext taskContext,
        Func<CameraSessionViewModel, SessionStartPrecheckResult> prepareStart,
        PropertyChangedEventHandler onCameraSessionPropertyChanged,
        Func<CameraSessionViewModel, PrimaryVisionTaskBinding?>? getBinding = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(cameraSettings);
        ArgumentNullException.ThrowIfNull(sopProfiles);
        ArgumentNullException.ThrowIfNull(prepareStart);
        ArgumentNullException.ThrowIfNull(onCameraSessionPropertyChanged);

        foreach (var session in sessions)
        {
            session.PropertyChanged -= onCameraSessionPropertyChanged;
            session.Dispose();
        }

        sessions.Clear();

        foreach (var profile in cameraSettings.Cameras)
        {
            var sopProfile = ResolveSopProfile(profile.SelectedSopProfileId, sopProfiles);
            var perCameraSteps = sopProfile?.Steps
                .Select(s => new FsmStepDefinition
                {
                    Step = s.Step,
                    Name = s.Name ?? string.Empty,
                    ActionCode = s.ActionCode,
                    TcnLabel = s.TcnLabel,
                    ExpectedStateCode = s.ExpectedStateCode
                })
                .ToList() ?? new List<FsmStepDefinition>();

            var perCameraConfig = new AnalysisConfig
            {
                EnableOnlineAnalysis = profile.EnableSopAnalysis && sopProfile != null && perCameraSteps.Count > 0,
                Strategy = sopProfile?.Strategy ?? AnalysisStrategyNames.SopRules,
                FrameWindowSize = profile.AnalysisFrameWindowSize,
                StateWindowSize = profile.AnalysisStateWindowSize,
                NearThresholdQ1000 = 300,
                NearStep = null,
                HoldFrames = profile.AnalysisHoldFrames,
                SopWindowMs = profile.SopWindowMs,
                SopMinScoreQ1000 = profile.SopMinScoreQ1000,
                SopMinVisibleRatioQ1000 = profile.SopMinVisibleRatioQ1000,
                SourceTaskId = string.IsNullOrWhiteSpace(profile.PrimaryTaskId) ? null : profile.PrimaryTaskId.Trim()
            };

            var session = new CameraSessionViewModel(
                profile,
                _cameraProviders,
                _resultWriter,
                _analysisResultWriter,
                _labelWriter,
                _featureWriter,
                perCameraConfig,
                perCameraSteps,
                _uiDispatcher,
                _pipelineSupportFactory,
                _visionTaskFactoryRegistry,
                prepareStart,
                getBinding,
                _modbusRegisters);
            session.PropertyChanged += onCameraSessionPropertyChanged;

            sessions.Add(session);
        }

        var selectedProfile = cameraSettings.ResolveSelectedCamera();
        var selectedSession = (selectedProfile != null
                                  ? sessions.FirstOrDefault(session =>
                                      string.Equals(session.Id, selectedProfile.Id, StringComparison.OrdinalIgnoreCase))
                                  : null)
                              ?? sessions.FirstOrDefault();

        return new SessionRebuildResult(selectedSession);
    }

    private static SopProfile? ResolveSopProfile(string? selectedSopProfileId, IReadOnlyList<SopProfile> sopProfiles)
    {
        if (string.IsNullOrWhiteSpace(selectedSopProfileId))
        {
            return null;
        }

        return sopProfiles.FirstOrDefault(p =>
            string.Equals(p.Id, selectedSopProfileId, StringComparison.OrdinalIgnoreCase));
    }

    public bool StartConfiguredSessions(
        IEnumerable<CameraProfile> profiles,
        IReadOnlyCollection<CameraSessionViewModel> sessions)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(sessions);

        bool started = false;
        foreach (var profile in profiles)
        {
            var session = sessions.FirstOrDefault(item =>
                string.Equals(item.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
            if (session == null)
            {
                continue;
            }

            // Fire-and-forget: model loading runs on background thread, UI stays responsive
            _ = session.StartAsync();
            started = true;
        }

        return started;
    }

    public void StopAllSessions(IEnumerable<CameraSessionViewModel> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        foreach (var session in sessions.Where(item => item.IsRunning))
        {
            session.Stop();
        }
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

    private static bool IsSharedRuntime(VisionTaskDefinition definition)
    {
        if (definition.Metadata.TryGetValue("shared", out var sharedRaw) &&
            bool.TryParse(sharedRaw, out var shared))
        {
            return shared;
        }

        return definition.Metadata.TryGetValue("runtime.shared", out var runtimeSharedRaw) &&
               bool.TryParse(runtimeSharedRaw, out var runtimeShared) &&
               runtimeShared;
    }

    private sealed class SharedVisionTaskHost
    {
        private readonly IVisionTask _task;
        private readonly object _sync = new();
        private readonly SemaphoreSlim _gate = new(1, 1);
        private int _leaseCount;
        private bool _disposed;

        public SharedVisionTaskHost(IVisionTask task)
        {
            _task = task ?? throw new ArgumentNullException(nameof(task));
        }

        public IVisionTask Acquire()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(SharedVisionTaskHost));
                }

                _leaseCount++;
            }

            return new SharedVisionTaskLease(this);
        }

        private void Release()
        {
            var shouldDispose = false;
            lock (_sync)
            {
                if (_leaseCount > 0)
                {
                    _leaseCount--;
                }

                if (_leaseCount == 0 && !_disposed)
                {
                    _disposed = true;
                    shouldDispose = true;
                }
            }

            if (shouldDispose)
            {
                _gate.Dispose();
                _task.Dispose();
            }
        }

        private void RunLocked(Action action)
        {
            _gate.Wait();
            try
            {
                action();
            }
            finally
            {
                _gate.Release();
            }
        }

        private T RunLocked<T>(Func<T> action)
        {
            _gate.Wait();
            try
            {
                return action();
            }
            finally
            {
                _gate.Release();
            }
        }

        private sealed class SharedVisionTaskLease : IVisionTask
        {
            private readonly SharedVisionTaskHost _host;
            private bool _disposed;

            public SharedVisionTaskLease(SharedVisionTaskHost host)
            {
                _host = host;
            }

            public string TaskId => _host._task.TaskId;
            public VisionTaskKind TaskKind => _host._task.TaskKind;
            public VisionRuntimeKind RuntimeKind => _host._task.RuntimeKind;
            public string? ActiveDeviceLabel => _host._task.ActiveDeviceLabel;

            public VisionTaskExecutionResult Execute(Mat image, VisionTaskExecutionContext context)
            {
                return _host.RunLocked(() => _host._task.Execute(image, context));
            }

            public void Warmup(int width, int height)
            {
                _host.RunLocked(() => _host._task.Warmup(width, height));
            }

            public void UpdateClassNames(string[]? classNames)
            {
                _host.RunLocked(() => _host._task.UpdateClassNames(classNames));
            }

            public bool TryHandleFailure(Exception ex, out string message)
            {
                string? localMessage = null;
                var handled = _host.RunLocked(() => _host._task.TryHandleFailure(ex, out localMessage));
                message = localMessage ?? string.Empty;
                return handled;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _host.Release();
            }
        }
    }
}
