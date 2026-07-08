using System.Globalization;
using NewLife.Log;

namespace VideoInferenceDemo;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        XTrace.UseConsole();
        JetsonHostOptions options;
        try
        {
            options = JetsonHostOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            PrintUsage();
            return 1;
        }

        if (options.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        var layout = JetsonHostLayout.Resolve(
            AppContext.BaseDirectory,
            options.CameraConfigPath,
            options.ModelRootPath,
            options.LogDirectory);

        if (options.RunDiagnosticsOnly)
        {
            var diagnostics = new JetsonEnvironmentDiagnosticsService(
                layout,
                options.OrtNativeLibraryPath,
                options.OrtProviderCsv);
            var result = diagnostics.Run();
            Console.WriteLine(result.Summary);
            Console.WriteLine(result.ReportPath);
            return result.State == EnvironmentDiagnosticsState.Error ? 10 : 0;
        }

        CameraProfile profile;
        try
        {
            profile = ResolveProfile(options, layout);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to resolve camera profile: {ex.Message}");
            return 2;
        }

        if (!string.Equals(profile.ProviderId, CameraProviderIds.OpenCv, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(profile.ProviderId, CameraProviderIds.HikRobot, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(
                    $"Jetson host currently supports providers '{CameraProviderIds.OpenCv}' and '{CameraProviderIds.HikRobot}', " +
                    $"but config requested '{profile.ProviderId}'.");
                return 3;
            }
        }

        var targetFps = options.TargetFpsOverride ?? profile.TargetFps;
        var recording = BuildRecordingOptions(profile, options);
        var providerRegistry = JetsonCameraProviderRegistry.CreateDefault();
        ConfigureOrtRuntime(options);

        using var pipeline = new VideoPipeline(providerRegistry);
        using var stopCts = new CancellationTokenSource();

        ModelBindingPlan modelPlan;
        InferenceModelDescriptor modelDescriptor;
        var visionTaskRegistry = new VisionTaskFactoryRegistry(new IVisionTaskFactory[]
        {
            OnnxVisionTaskFactory.Instance
        });
        try
        {
            modelPlan = ResolveModelBindingPlan(options, layout);
            modelDescriptor = ModelPipelineFactory.CreateDescriptor(modelPlan, InferenceDeviceKind.GpuCuda, options.ConfidenceThreshold, options.NmsThreshold);
            var taskDefinition = ModelCatalogVisionTaskMapper.ToVisionTaskDefinition(modelPlan.Model);
            var taskContext = new VisionTaskCreationContext(InferenceDeviceKind.GpuCuda, options.ConfidenceThreshold, options.NmsThreshold);
            var task = visionTaskRegistry.Create(taskDefinition, taskContext);
            pipeline.UpdateDetectionDrawOptions(
                modelPlan.BoxColor,
                modelPlan.BoxColors,
                modelPlan.BoxThickness,
                modelPlan.LabelFontScale);
            pipeline.SetPrimaryTask(task, modelDescriptor.ModelPath, clearSidecars: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to resolve model binding plan: {ex.Message}");
            return 4;
        }

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stopCts.Cancel();
        };

        var lastStatsPrintedUtc = DateTimeOffset.MinValue;
        pipeline.Error += message => Console.Error.WriteLine($"[pipeline-error] {message}");
        pipeline.RunEnded += ended => Console.WriteLine($"[pipeline-ended] run={ended.RunUuid} reason={ended.Reason}");
        pipeline.StatsUpdated += stats =>
        {
            var now = DateTimeOffset.UtcNow;
            if ((now - lastStatsPrintedUtc) < TimeSpan.FromSeconds(2))
            {
                return;
            }

            lastStatsPrintedUtc = now;
            Console.WriteLine(
                $"[stats] camera=\"{profile.Name}\" fps(c/i/r)={stats.CaptureFps:F2}/{stats.InferFps:F2}/{stats.RenderFps:F2} " +
                $"queue={stats.FrameQueueSize}/{stats.RenderQueueSize} pts={stats.CurrentPtsMs} " +
                $"drop={stats.DroppedByCaptureQueue}/{stats.DroppedByInferDrain}/{stats.DroppedByRenderQueue}");
        };

        Console.WriteLine(
            $"Starting Jetson headless host. camera=\"{profile.Name}\", provider={profile.ProviderId}, " +
            $"index={profile.CameraIndex}, fps={targetFps:F2}, recording={recording.Enabled}, root=\"{recording.RootDirectory}\", " +
            $"model=\"{modelDescriptor.Model.DisplayName}\", ortProviders={DescribeOrtProviders(options.OrtProviderCsv)}, " +
            $"logDir=\"{layout.LogDirectory}\"");

        pipeline.StartCamera(profile.BuildOpenOptions(targetFps), targetFps, recording);

        try
        {
            if (options.DurationSeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(options.DurationSeconds), stopCts.Token);
            }
            else
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, stopCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Console.WriteLine("Stopping pipeline...");
            pipeline.Stop();
        }

        return 0;
    }

    private static CameraProfile ResolveProfile(JetsonHostOptions options, JetsonHostLayout layout)
    {
        if (options.CameraIndexOverride.HasValue)
        {
            var profile = CameraProfile.CreateDefault(1);
            profile.Name = $"Camera {options.CameraIndexOverride.Value}";
            profile.ProviderId = CameraProviderIds.OpenCv;
            profile.CameraIndex = options.CameraIndexOverride.Value;
            profile.OpenCvSource = options.CameraSourceOverride ?? string.Empty;
            profile.OpenCvBackend = options.OpenCvBackendOverride ?? string.Empty;
            profile.TargetFps = options.TargetFpsOverride ?? profile.TargetFps;
            return profile.Normalize(1);
        }

        var settings = CameraSettingsStorage.Load(layout.CameraConfigPath);
        var cameras = settings.Cameras ?? new List<CameraProfile>();

        CameraProfile? selected = null;
        if (!string.IsNullOrWhiteSpace(options.CameraIdOrName))
        {
            selected = cameras.FirstOrDefault(camera =>
                string.Equals(camera.Id, options.CameraIdOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(camera.Name, options.CameraIdOrName, StringComparison.OrdinalIgnoreCase));
        }

        selected ??= cameras.FirstOrDefault(camera => camera.Enabled && camera.AutoStart);
        selected ??= cameras.FirstOrDefault(camera => camera.Enabled);
        selected ??= cameras.FirstOrDefault();

        if (selected == null)
        {
            throw new InvalidOperationException($"No camera profile was found in '{layout.CameraConfigPath}'.");
        }

        if (!string.IsNullOrWhiteSpace(options.CameraSourceOverride))
        {
            selected.OpenCvSource = options.CameraSourceOverride;
        }

        if (!string.IsNullOrWhiteSpace(options.OpenCvBackendOverride))
        {
            selected.OpenCvBackend = options.OpenCvBackendOverride;
        }

        return selected.Normalize(1);
    }

    private static CameraRecordingOptions BuildRecordingOptions(CameraProfile profile, JetsonHostOptions options)
    {
        var recording = profile.BuildRecordingOptions();

        if (!string.IsNullOrWhiteSpace(options.RecordRootOverride))
        {
            recording.RootDirectory = options.RecordRootOverride;
        }

        if (options.EnableRecordingOverride.HasValue)
        {
            recording.Enabled = options.EnableRecordingOverride.Value;
        }

        return recording.Normalize();
    }

    private static ModelBindingPlan ResolveModelBindingPlan(JetsonHostOptions options, JetsonHostLayout layout)
    {
        var models = ModelCatalog.Discover(layout.ModelRootPath);
        if (models.Count == 0)
        {
            throw new InvalidOperationException($"No model bundle was found in '{layout.ModelRootPath}'.");
        }

        ModelCatalogEntry? selected = null;
        if (!string.IsNullOrWhiteSpace(options.ModelIdOrName))
        {
            selected = models.FirstOrDefault(model =>
                string.Equals(model.Id, options.ModelIdOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(model.DisplayName, options.ModelIdOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileNameWithoutExtension(model.ModelFileName), options.ModelIdOrName, StringComparison.OrdinalIgnoreCase));
        }

        selected ??= models.FirstOrDefault();
        if (selected == null)
        {
            throw new InvalidOperationException($"No model bundle was found in '{layout.ModelRootPath}'.");
        }

        return ModelBindingPlanFactory.Create(selected);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("VideoInference.Jetson");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project src/VideoInference.Jetson -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --camera-config <path>    Camera config json path. Default: ./camera_config.json");
        Console.WriteLine("  --camera <id-or-name>     Camera id or camera name from config.");
        Console.WriteLine("  --camera-index <index>    Override config and use OpenCV camera index directly.");
        Console.WriteLine("  --camera-source <value>   Override config and use an OpenCV source string (file, RTSP, GStreamer, CSI, etc.).");
        Console.WriteLine("  --opencv-backend <value>  Optional OpenCV backend. Example: gstreamer, ffmpeg, v4l2.");
        Console.WriteLine("  --log-dir <path>          Diagnostics/log output directory. Default: ./logs");
        Console.WriteLine("  --fps <value>             Override target fps.");
        Console.WriteLine("  --duration <seconds>      Stop automatically after N seconds.");
        Console.WriteLine("  --record-root <path>      Override recording root directory.");
        Console.WriteLine("  --record                  Force enable recording.");
        Console.WriteLine("  --no-record               Force disable recording.");
        Console.WriteLine("  --model-root <path>       Model bundle root. Default: ./DL");
        Console.WriteLine("  --model <id-or-name>      Model id or display name from model catalog.");
        Console.WriteLine("  --conf <value>            Detection confidence threshold. Default: 0.25");
        Console.WriteLine("  --iou <value>             Detection NMS IoU threshold. Default: 0.45");
        Console.WriteLine("  --ort-native <path>       Optional ONNX Runtime library or directory.");
        Console.WriteLine("  --providers <csv>         ORT provider order. Example: tensorrt,cuda,cpu");
        Console.WriteLine("  --device-id <id>          GPU device id for CUDA/TensorRT providers.");
        Console.WriteLine("  --trt-fp16                Enable TensorRT FP16.");
        Console.WriteLine("  --trt-engine-cache        Enable TensorRT engine cache.");
        Console.WriteLine("  --trt-engine-cache-path <path> TensorRT engine cache directory.");
        Console.WriteLine("  --diagnose                Run Linux environment diagnostics and exit.");
        Console.WriteLine("  --help                    Show this message.");
    }

    private static void ConfigureOrtRuntime(JetsonHostOptions options)
    {
        var providerOrder = OrtExecutionProviderParser.ParseCsv(options.OrtProviderCsv);
        OrtRuntimeEnvironment.Configure(new OrtRuntimeEnvironmentOptions
        {
            NativeLibraryPath = options.OrtNativeLibraryPath,
            GpuProviderOrder = providerOrder.Count > 0 ? providerOrder : null,
            DeviceId = Math.Max(0, options.OrtDeviceId),
            TensorRtFp16 = options.EnableTensorRtFp16,
            TensorRtEngineCache = options.EnableTensorRtEngineCache,
            TensorRtEngineCachePath = options.TensorRtEngineCachePath
        });
    }

    private static string DescribeOrtProviders(string? providerCsv)
    {
        var providerOrder = OrtExecutionProviderParser.ParseCsv(providerCsv);
        if (providerOrder.Count == 0)
        {
            var defaults = OrtSessionFactory.ResolveProviderOrder(new OrtSessionFactoryOptions
            {
                DeviceKind = InferenceDeviceKind.GpuCuda
            });
            return string.Join("->", defaults);
        }

        return string.Join("->", providerOrder);
    }
}

internal sealed record JetsonHostOptions(
    bool ShowHelp,
    bool RunDiagnosticsOnly,
    string? CameraConfigPath,
    string? CameraIdOrName,
    int? CameraIndexOverride,
    string? CameraSourceOverride,
    string? OpenCvBackendOverride,
    string? LogDirectory,
    double? TargetFpsOverride,
    int DurationSeconds,
    string? RecordRootOverride,
    bool? EnableRecordingOverride,
    string? ModelRootPath,
    string? ModelIdOrName,
    float ConfidenceThreshold,
    float NmsThreshold,
    string? OrtNativeLibraryPath,
    string? OrtProviderCsv,
    int OrtDeviceId,
    bool EnableTensorRtFp16,
    bool EnableTensorRtEngineCache,
    string? TensorRtEngineCachePath)
{
    public static JetsonHostOptions Parse(string[] args)
    {
        string? cameraConfigPath = null;
        string? cameraIdOrName = null;
        int? cameraIndex = null;
        string? cameraSource = null;
        string? openCvBackend = null;
        string? logDirectory = null;
        double? fps = null;
        var durationSeconds = 0;
        string? recordRoot = null;
        bool? enableRecording = null;
        string? modelRootPath = null;
        string? modelIdOrName = null;
        var confidenceThreshold = 0.25f;
        var nmsThreshold = 0.45f;
        string? ortNativeLibraryPath = null;
        string? ortProviderCsv = null;
        var ortDeviceId = 0;
        var enableTensorRtFp16 = false;
        var enableTensorRtEngineCache = false;
        string? tensorRtEngineCachePath = null;
        var showHelp = false;
        var runDiagnosticsOnly = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                case "--camera-config":
                    cameraConfigPath = ReadValue(args, ref i, arg);
                    break;
                case "--camera":
                    cameraIdOrName = ReadValue(args, ref i, arg);
                    break;
                case "--camera-index":
                    cameraIndex = int.Parse(ReadValue(args, ref i, arg), CultureInfo.InvariantCulture);
                    break;
                case "--camera-source":
                    cameraSource = ReadValue(args, ref i, arg);
                    break;
                case "--opencv-backend":
                    openCvBackend = ReadValue(args, ref i, arg);
                    break;
                case "--log-dir":
                    logDirectory = ReadValue(args, ref i, arg);
                    break;
                case "--fps":
                    fps = double.Parse(ReadValue(args, ref i, arg), CultureInfo.InvariantCulture);
                    break;
                case "--duration":
                    durationSeconds = int.Parse(ReadValue(args, ref i, arg), CultureInfo.InvariantCulture);
                    break;
                case "--record-root":
                    recordRoot = ReadValue(args, ref i, arg);
                    break;
                case "--record":
                    enableRecording = true;
                    break;
                case "--no-record":
                    enableRecording = false;
                    break;
                case "--model-root":
                    modelRootPath = ReadValue(args, ref i, arg);
                    break;
                case "--model":
                    modelIdOrName = ReadValue(args, ref i, arg);
                    break;
                case "--conf":
                    confidenceThreshold = float.Parse(ReadValue(args, ref i, arg), CultureInfo.InvariantCulture);
                    break;
                case "--iou":
                    nmsThreshold = float.Parse(ReadValue(args, ref i, arg), CultureInfo.InvariantCulture);
                    break;
                case "--ort-native":
                    ortNativeLibraryPath = ReadValue(args, ref i, arg);
                    break;
                case "--providers":
                    ortProviderCsv = ReadValue(args, ref i, arg);
                    break;
                case "--device-id":
                    ortDeviceId = int.Parse(ReadValue(args, ref i, arg), CultureInfo.InvariantCulture);
                    break;
                case "--trt-fp16":
                    enableTensorRtFp16 = true;
                    break;
                case "--trt-engine-cache":
                    enableTensorRtEngineCache = true;
                    break;
                case "--trt-engine-cache-path":
                    tensorRtEngineCachePath = ReadValue(args, ref i, arg);
                    break;
                case "--diagnose":
                    runDiagnosticsOnly = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{arg}'.");
            }
        }

        return new JetsonHostOptions(
            showHelp,
            runDiagnosticsOnly,
            cameraConfigPath,
            cameraIdOrName,
            cameraIndex,
            cameraSource,
            openCvBackend,
            logDirectory,
            fps,
            Math.Max(0, durationSeconds),
            recordRoot,
            enableRecording,
            modelRootPath,
            modelIdOrName,
            Math.Clamp(confidenceThreshold, 0.001f, 1.0f),
            Math.Clamp(nmsThreshold, 0.001f, 1.0f),
            ortNativeLibraryPath,
            ortProviderCsv,
            Math.Max(0, ortDeviceId),
            enableTensorRtFp16,
            enableTensorRtEngineCache,
            tensorRtEngineCachePath);
    }

    private static string ReadValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Option '{optionName}' requires a value.");
        }

        index++;
        return args[index];
    }
}
