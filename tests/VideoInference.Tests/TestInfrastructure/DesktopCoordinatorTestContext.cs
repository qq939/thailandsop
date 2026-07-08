using System.Text.Json;
using Xunit;

namespace VideoInferenceDemo.Tests;

[CollectionDefinition("DbSession", DisableParallelization = true)]
public sealed class DbSessionTestCollection; // NOSONAR - collection definition marker

internal sealed class DesktopCoordinatorTestContext : IDisposable
{
    private readonly List<IDisposable> _disposables = new();
    private readonly ImmediateUiDispatcher _uiDispatcher = new();
    private readonly StubDesktopPipelineSupportFactory _pipelineSupportFactory = new();

    public DesktopCoordinatorTestContext()
    {
        RootDirectory = Path.Combine(
            Path.GetTempPath(),
            "VideoInferenceDemo.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(Path.Combine(RootDirectory, "DL"));

        var workspaceDbPath = Path.Combine(RootDirectory, "workspace.db");
        DbSession.Initialize(workspaceDbPath);

        ResultWriter = new SqliteResultWriter();
        AnalysisResultWriter = new SqliteAnalysisResultWriter();
        LabelWriter = new TcnLabelWriter();

        Register(ResultWriter);
        Register(AnalysisResultWriter);
        Register(LabelWriter);

        SessionTaskOrchestrator = new SessionTaskOrchestrator(
            new CameraProviderRegistry(Array.Empty<CameraProviderRegistration>()),
            ResultWriter,
            AnalysisResultWriter,
            LabelWriter,
            null,
            _uiDispatcher,
            _pipelineSupportFactory,
            new VisionTaskFactoryRegistry(new[] { new TestVisionTaskFactory() }));

        WorkspaceRunCoordinator = new WorkspaceRunCoordinator(
            new PersonnelRepository(workspaceDbPath),
            new RunOperatorAssignmentRepository(workspaceDbPath),
            new RunProductionStatsRepository(workspaceDbPath));

        CameraSessionWorkspaceCoordinator = new CameraSessionWorkspaceCoordinator(
            SessionTaskOrchestrator,
            WorkspaceRunCoordinator);

        WorkspaceSelectionCoordinator = new WorkspaceSelectionCoordinator(SessionTaskOrchestrator);
    }

    public string RootDirectory { get; }
    public AnalysisConfig AnalysisConfig { get; } = new();
    public IReadOnlyList<SopProfile> DefaultSopProfiles { get; } = new[]
    {
        new SopProfile
        {
            Id = "sop-default",
            Name = "Default SOP",
            Strategy = AnalysisStrategyNames.SopRules,
            Steps = new List<FsmStepDefinition>
            {
                new() { Step = 1, Name = "Step 1" },
                new() { Step = 2, Name = "Step 2" }
            }
        }
    };

    public SqliteResultWriter ResultWriter { get; }
    public SqliteAnalysisResultWriter AnalysisResultWriter { get; }
    public TcnLabelWriter LabelWriter { get; }
    public SessionTaskOrchestrator SessionTaskOrchestrator { get; }
    public WorkspaceRunCoordinator WorkspaceRunCoordinator { get; }
    public CameraSessionWorkspaceCoordinator CameraSessionWorkspaceCoordinator { get; }
    public WorkspaceSelectionCoordinator WorkspaceSelectionCoordinator { get; }

    public CameraSessionViewModel CreateSession(
        CameraProfile? profile = null,
        Func<CameraSessionViewModel, SessionStartPrecheckResult>? prepareStart = null)
    {
        var defaultSteps = DefaultSopProfiles.FirstOrDefault()?.Steps
            ?? new List<FsmStepDefinition>
            {
                new() { Step = 1, Name = "Step 1" },
                new() { Step = 2, Name = "Step 2" }
            };

        var session = new CameraSessionViewModel(
            profile ?? CreateCameraProfile(),
            new CameraProviderRegistry(Array.Empty<CameraProviderRegistration>()),
            ResultWriter,
            AnalysisResultWriter,
            LabelWriter,
            null,
            AnalysisConfig,
            defaultSteps,
            _uiDispatcher,
            _pipelineSupportFactory,
            new VisionTaskFactoryRegistry(new[] { new TestVisionTaskFactory() }),
            prepareStart);
        Register(session);
        return session;
    }

    public CameraProfile CreateCameraProfile(
        string? id = null,
        string? name = null,
        bool enabled = true,
        bool autoStart = false)
    {
        return new CameraProfile
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            Name = name ?? "Camera",
            Enabled = enabled,
            AutoStart = autoStart,
            ProviderId = CameraProviderIds.OpenCv,
            CameraIndex = 0,
            TargetFps = 30
        };
    }

    public VisionWorkspaceService CreateWorkspaceService()
    {
        return new VisionWorkspaceService(RootDirectory);
    }

    public string CreateSpecialTaskBundle(
        string id,
        string displayName,
        VisionTaskKind taskKind = VisionTaskKind.HandLandmarks,
        VisionRuntimeKind runtimeKind = VisionRuntimeKind.MediaPipe,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var bundleDirectory = Path.Combine(RootDirectory, "DL", id);
        Directory.CreateDirectory(bundleDirectory);

        var payload = new
        {
            id,
            displayName,
            taskKind = taskKind.ToString(),
            runtimeKind = runtimeKind.ToString(),
            metadata
        };

        File.WriteAllText(
            Path.Combine(bundleDirectory, "task.json"),
            JsonSerializer.Serialize(payload));

        return bundleDirectory;
    }

    public string CreateModelBundle(
        string id,
        string displayName,
        string? description = null)
    {
        var bundleDirectory = Path.Combine(RootDirectory, "DL", id);
        Directory.CreateDirectory(bundleDirectory);

        var modelPath = Path.Combine(bundleDirectory, "model.onnx");
        File.WriteAllBytes(modelPath, new byte[] { 0 });
        File.WriteAllText(
            Path.Combine(bundleDirectory, "model.json"),
            JsonSerializer.Serialize(new
            {
                id,
                displayName,
                description = description ?? "Test model",
                taskType = "detection",
                modelFile = "model.onnx"
            }));

        return modelPath;
    }

    public static VisionTaskCreationContext CreateTaskContext(
        InferenceDeviceKind deviceKind = InferenceDeviceKind.Cpu)
    {
        return new VisionTaskCreationContext(deviceKind, 0.4f, 0.5f);
    }

    public void Dispose()
    {
        for (var i = _disposables.Count - 1; i >= 0; i--)
        {
            _disposables[i].Dispose();
        }

        DbSession.Reset();

        try
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private void Register(IDisposable disposable)
    {
        _disposables.Add(disposable);
    }

    private sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();

        public void Render(Action action) => action();
    }

    private sealed class StubDesktopPipelineSupportFactory : IDesktopPipelineSupportFactory
    {
        public TcnOnnxInferenceEngine? TryCreateTcnEngine() => null;

        public IVisionResultSink BuildCompatibilityVisionResultSink(
            SqliteResultWriter resultWriter,
            AnalysisEngine? analysisEngine,
            TcnFeatureWriter? featureWriter,
            TcnOnnxInferenceEngine? tcnEngine)
        {
            return new NoOpVisionResultSink();
        }
    }

    private sealed class NoOpVisionResultSink : IVisionResultSink
    {
        public bool TryPublish(VisionFrameResult result) => true;
    }

    private sealed class TestVisionTaskFactory : IVisionTaskFactory
    {
        public bool CanCreate(VisionTaskDefinition definition) => true;

        public IVisionTask Create(VisionTaskDefinition definition, VisionTaskCreationContext context)
        {
            throw new NotSupportedException("Tests only verify binding/materialization boundaries.");
        }
    }
}
