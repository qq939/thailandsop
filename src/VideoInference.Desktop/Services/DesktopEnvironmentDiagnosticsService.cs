using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace VideoInferenceDemo;

public sealed class DesktopEnvironmentDiagnosticsService : IEnvironmentDiagnosticsService
{
    public static string ReportPath => Path.Combine(AppContext.BaseDirectory, "environment_diagnostics.log");

    public EnvironmentDiagnosticsResult Run()
    {
        var lines = new List<string>();
        var checks = new List<DiagnosticsCheckResult>();
        var utcNow = DateTimeOffset.UtcNow;
        Append(lines, $"Environment diagnostics started at {utcNow:yyyy-MM-dd HH:mm:ss} UTC");
        Append(lines, $"BaseDirectory: {AppContext.BaseDirectory}");
        Append(lines, $"OS: {RuntimeInformation.OSDescription}");
        Append(lines, $"ProcessArchitecture: {RuntimeInformation.ProcessArchitecture}");
        Append(lines, $"Framework: {RuntimeInformation.FrameworkDescription}");

        var layout = DesktopNativeRuntimePathResolver.Resolve(AppContext.BaseDirectory);
        var models = DiscoverModels();
        var tensorRtProbeModel = FindFirstOnnxModelWithTensorRtCache(models);
        var probesTensorRt = tensorRtProbeModel != null;
        var processPathEntries = ReadProcessPathEntries();

        Append(lines, string.Empty);
        Append(lines, "[Resolved Paths]");
        Append(lines, $"ORT native: {DescribePath(layout.OrtNativeDir)}");
        Append(lines, $"ThirdParty: {DescribePath(layout.ThirdPartyDir)}");
        Append(lines, $"CameraHIK: {DescribePath(layout.CameraHikDir)}");
        Append(lines, $"MVS Win64 runtime: {DescribePath(layout.MvsRuntimeX64)}");
        Append(lines, $"CUDA bin: {DescribePath(layout.CudaBin)}");
        Append(lines, $"cuDNN bin: {DescribePath(layout.CudnnBin)}");
        Append(lines, $"TensorRT root: {DescribePath(layout.TensorRtRoot)}");
        Append(lines, $"TensorRT bin: {DescribePath(layout.TensorRtBin)}");
        Append(lines, $"TensorRT lib: {DescribePath(layout.TensorRtLib)}");

        checks.Add(CreatePathCheck("Runtime", "ORT native", layout.OrtNativeDir));
        checks.Add(CreatePathCheck("Runtime", "CameraHIK", layout.CameraHikDir));
        checks.Add(CreatePathCheck("Runtime", "MVS Win64 runtime", layout.MvsRuntimeX64));
        checks.Add(CreatePathCheck("Runtime", "CUDA bin", layout.CudaBin));
        checks.Add(CreatePathCheck("Runtime", "cuDNN bin", layout.CudnnBin));
        checks.Add(CreatePathCheck("Runtime", "TensorRT root", layout.TensorRtRoot, probesTensorRt ? DiagnosticsSeverity.Warning : DiagnosticsSeverity.Info));
        checks.Add(CreatePathCheck("Runtime", "TensorRT bin", layout.TensorRtBin, probesTensorRt ? DiagnosticsSeverity.Warning : DiagnosticsSeverity.Info));
        checks.Add(CreatePathCheck("Runtime", "TensorRT lib", layout.TensorRtLib, probesTensorRt ? DiagnosticsSeverity.Warning : DiagnosticsSeverity.Info));

        Append(lines, string.Empty);
        Append(lines, "[Environment Variables]");
        var cudaPath = LogEnvironmentVariable(lines, checks, "CUDA_PATH");
        var tensorRtPath = LogEnvironmentVariable(lines, checks, "TENSORRT_PATH");
        var tensorRtRoot = LogEnvironmentVariable(lines, checks, "TENSORRT_ROOT");
        _ = LogEnvironmentVariable(lines, checks, "TensorRT_ROOT");
        LogPathMatches(lines, checks, "TensorRT", processPathEntries, probesTensorRt, "TensorRT", "nvinfer");
        LogPathMatches(lines, checks, "CUDA", processPathEntries, true, @"\CUDA\", "cudart64_");
        LogPathMatches(lines, checks, "cuDNN", processPathEntries, true, @"\CUDNN\", "cudnn");
        LogPathMatches(lines, checks, "MVS", processPathEntries, false, @"\Common Files\MVS\", "MvCameraControl");
        LogEnvironmentConsistencyWarnings(lines, checks, layout, probesTensorRt, cudaPath, tensorRtPath, tensorRtRoot);

        Append(lines, string.Empty);
        Append(lines, "[ORT Runtime Selection]");
        var diagnosticProviderOrder = BuildDiagnosticProviderOrder(probesTensorRt);
        var formattedProviders = FormatProviderOrder(diagnosticProviderOrder);
        Append(lines, $"Providers: {formattedProviders}");
        Append(lines, probesTensorRt
            ? $"TensorRT cache: {ResolveTensorRtCachePath(tensorRtProbeModel!)}"
            : "TensorRT cache: <none discovered for ONNX models; probe will use CUDA -> CPU>");
        checks.Add(new DiagnosticsCheckResult("ORT Config", "Requested providers", DiagnosticsSeverity.Info, formattedProviders));

        Append(lines, string.Empty);
        Append(lines, "[Key Files]");
        LogFile(lines, checks, "Files", Path.Combine(layout.OrtNativeDir, "onnxruntime.dll"));
        LogFile(lines, checks, "Files", Path.Combine(layout.OrtNativeDir, "onnxruntime_providers_shared.dll"));
        LogFile(lines, checks, "Files", Path.Combine(layout.OrtNativeDir, "onnxruntime_providers_cuda.dll"));
        LogFile(lines, checks, "Files", Path.Combine(layout.OrtNativeDir, "onnxruntime_providers_tensorrt.dll"), probesTensorRt ? DiagnosticsSeverity.Warning : DiagnosticsSeverity.Info);
        LogFile(lines, checks, "Files", Path.Combine(layout.OrtNativeDir, "zlibwapi.dll"));
        LogFile(lines, checks, "Files", Path.Combine(layout.CameraHikDir, "MvCameraControl.Net.dll"));
        LogFile(lines, checks, "Files", Path.Combine(layout.MvsRuntimeX64, "MvCameraControl.dll"));
        LogFile(lines, checks, "Files", Path.Combine(layout.MvsRuntimeX64, "MvUsb3vTL.dll"));
        LogFile(lines, checks, "Files", Path.Combine(layout.MvsRuntimeX64, "MVGigEVisionSDK.dll"));
        LogFile(lines, checks, "Files", Path.Combine(layout.CudaBin, "cudart64_12.dll"));
        LogFile(lines, checks, "Files", Path.Combine(layout.CudaBin, "cublas64_12.dll"));
        LogFile(lines, checks, "Files", Path.Combine(layout.CudaBin, "cublasLt64_12.dll"));
        LogFile(lines, checks, "Files", Path.Combine(layout.CudnnBin, "cudnn64_9.dll"));
        LogFile(lines, checks, "Files", Path.Combine(layout.CudnnBin, "cudnn_ops64_9.dll"));
        LogFile(lines, checks, "Files", Path.Combine(layout.CudnnBin, "cudnn_cnn64_9.dll"));
        LogFile(lines, checks, "Files", Path.Combine(layout.CudnnBin, "cudnn_adv64_9.dll"));
        var tensorRtRuntimeFiles = FindTensorRtRuntimeFiles(layout);
        if (probesTensorRt && tensorRtRuntimeFiles.Count == 0)
        {
            Append(lines, "TensorRT runtime files: none discovered under the resolved bin/lib directories.");
            checks.Add(new DiagnosticsCheckResult(
                "Files",
                "TensorRT runtime discovery",
                DiagnosticsSeverity.Warning,
                "No TensorRT runtime DLLs were discovered under the resolved bin/lib directories."));
        }
        else
        {
            Append(lines, $"TensorRT runtime files discovered: {tensorRtRuntimeFiles.Count}");
            checks.Add(new DiagnosticsCheckResult(
                "Files",
                "TensorRT runtime discovery",
                DiagnosticsSeverity.Info,
                $"Discovered {tensorRtRuntimeFiles.Count} TensorRT runtime DLL(s)."));
        }

        foreach (var tensorRtFile in tensorRtRuntimeFiles)
        {
            LogFile(lines, checks, "Files", tensorRtFile, probesTensorRt ? DiagnosticsSeverity.Warning : DiagnosticsSeverity.Info);
        }

        Append(lines, string.Empty);
        Append(lines, "[LoadLibrary]");
        foreach (var candidate in BuildLoadLibraryCandidates(layout, tensorRtRuntimeFiles, probesTensorRt))
        {
            LogLoadLibrary(lines, checks, candidate);
        }

        Append(lines, string.Empty);
        Append(lines, "[Runtime Init]");
        LogRuntimeInitFiltering(lines, checks, layout);

        Append(lines, string.Empty);
        Append(lines, "[TensorRT DLL Versions]");
        foreach (var dll in tensorRtRuntimeFiles)
        {
            LogTensorRtVersion(lines, dll);
        }

        Append(lines, string.Empty);
        Append(lines, "[Provider Init Log]");
        LogProviderInitLogPath(lines, checks);

        Append(lines, string.Empty);
        Append(lines, "[Model Discovery]");
        if (models.Count == 0)
        {
            Append(lines, "No model bundles were discovered.");
            checks.Add(new DiagnosticsCheckResult("Models", "Discovery", DiagnosticsSeverity.Warning, "No model bundles were discovered."));
        }
        else
        {
            checks.Add(new DiagnosticsCheckResult("Models", "Discovery", DiagnosticsSeverity.Info, $"Discovered {models.Count} model bundle(s)."));
            foreach (var model in models)
            {
                Append(lines, $"{model.Id} | {model.DisplayName} | {model.TaskType} | {model.ModelPath}");
            }
        }

        Append(lines, string.Empty);
        Append(lines, "[ORT Session Test]");
        var ortSummary = RunOrtSessionProbe(lines, checks, models, layout, tensorRtProbeModel);

        Append(lines, string.Empty);
        Append(lines, "[Recommendations]");
        var recommendations = BuildRecommendations(checks, layout, probesTensorRt);
        if (recommendations.Count == 0)
        {
            Append(lines, "No immediate environment issue was detected.");
        }
        else
        {
            foreach (var recommendation in recommendations)
            {
                Append(lines, $"- {recommendation}");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)!);
        File.WriteAllText(ReportPath, string.Join(Environment.NewLine, lines), Encoding.UTF8);

        var report = new EnvironmentDiagnosticsReport(utcNow, checks);
        var state = report.Checks.Any(check => check.Severity == DiagnosticsSeverity.Error)
            ? EnvironmentDiagnosticsState.Error
            : report.Checks.Any(check => check.Severity == DiagnosticsSeverity.Warning)
                ? EnvironmentDiagnosticsState.Warning
                : EnvironmentDiagnosticsState.Success;
        var errors = report.Checks.Count(check => check.Severity == DiagnosticsSeverity.Error);
        var warnings = report.Checks.Count(check => check.Severity == DiagnosticsSeverity.Warning);
        var summary = warnings == 0 && errors == 0
            ? ortSummary.Message
            : $"{ortSummary.Message} Errors={errors}, warnings={warnings}.";

        return new EnvironmentDiagnosticsResult(
            state,
            summary,
            ReportPath,
            report);
    }

    private static (bool Success, string Message) RunOrtSessionProbe(
        List<string> lines,
        List<DiagnosticsCheckResult> checks,
        IReadOnlyList<ModelCatalogEntry> models,
        DesktopNativeRuntimeLayout layout,
        ModelCatalogEntry? tensorRtProbeModel)
    {
        var probeModel = tensorRtProbeModel;
        probeModel ??= models.FirstOrDefault(model =>
            Path.GetExtension(model.ModelPath).Equals(".onnx", StringComparison.OrdinalIgnoreCase));
        if (probeModel == null)
        {
            Append(lines, "Skipped: no ONNX model bundle was found.");
            checks.Add(new DiagnosticsCheckResult("ORT", "Session Probe", DiagnosticsSeverity.Warning, "Skipped: no ONNX model bundle was found."));
            return (false, "Diagnostics completed, but no ONNX model was available for probing.");
        }

        Append(lines, $"Probe model: {probeModel.DisplayName} ({probeModel.ModelPath})");
        try
        {
            var cachePath = ResolveTensorRtCachePath(probeModel);
            var hasModelTensorRtCache = HasTensorRtCache(probeModel);
            var requestedProviders = BuildDiagnosticProviderOrder(hasModelTensorRtCache);

            if (!hasModelTensorRtCache)
            {
                Append(lines, $"TensorRT provider excluded from probe (no cache for model '{probeModel.Id}').");
            }

            Append(lines, $"Requested providers for probe: {FormatProviderOrder(requestedProviders)}");
            var bundle = OrtSessionFactory.Create(
                probeModel.ModelPath,
                new OrtSessionFactoryOptions
                {
                    DeviceKind = InferenceDeviceKind.GpuCuda,
                    ProviderOrder = requestedProviders,
                    NativeLibraryPath = layout.OrtNativeDir,
                    TensorRtFp16 = hasModelTensorRtCache,
                    TensorRtEngineCache = hasModelTensorRtCache,
                    TensorRtEngineCachePath = hasModelTensorRtCache ? cachePath : null
                });

            using (bundle.Session)
            {
                Append(lines, $"Selected provider: {bundle.SelectedProvider}");
                var expectedProvider = requestedProviders.FirstOrDefault();
                var matchedRequestedPrimary = bundle.SelectedProvider == expectedProvider;
                checks.Add(new DiagnosticsCheckResult(
                    "ORT",
                    "Selected provider",
                    matchedRequestedPrimary ? DiagnosticsSeverity.Info : DiagnosticsSeverity.Warning,
                    bundle.SelectedProvider.ToString()));

                if (!matchedRequestedPrimary)
                {
                    checks.Add(new DiagnosticsCheckResult(
                        "ORT",
                        "Provider fallback",
                        DiagnosticsSeverity.Warning,
                        $"Requested primary provider {expectedProvider}, actual provider {bundle.SelectedProvider}."));
                }

                // Log which lower-priority providers had to fail before the selected one
                LogProviderFallbackChain(lines, checks, requestedProviders, bundle.SelectedProvider);

                return matchedRequestedPrimary
                    ? (true, $"Diagnostics completed. {bundle.SelectedProvider} inference is available.")
                    : (false, $"Diagnostics completed, but provider fallback occurred to {bundle.SelectedProvider}.");
            }
        }
        catch (Exception ex)
        {
            Append(lines, $"ORT session probe failed:{Environment.NewLine}{ex}");
            checks.Add(new DiagnosticsCheckResult("ORT", "Session Probe", DiagnosticsSeverity.Error, ex.Message));

            // Split exception message into individual provider failures
            LogProviderFailuresFromException(lines, checks, ex);

            return (false, $"Diagnostics completed, but ORT initialization failed: {ex.Message}");
        }
    }

    private static IReadOnlyList<ModelCatalogEntry> DiscoverModels()
    {
        return ModelWorkspaceService.DiscoverAvailableModels(AppContext.BaseDirectory);
    }

    private static ModelCatalogEntry? FindFirstOnnxModelWithTensorRtCache(IReadOnlyList<ModelCatalogEntry> models)
    {
        return models.FirstOrDefault(model =>
            Path.GetExtension(model.ModelPath).Equals(".onnx", StringComparison.OrdinalIgnoreCase) &&
            HasTensorRtCache(model));
    }

    private static bool HasTensorRtCache(ModelCatalogEntry model)
    {
        var cachePath = ResolveTensorRtCachePath(model);
        return Directory.Exists(cachePath) &&
               Directory.EnumerateFiles(cachePath, "*", SearchOption.AllDirectories).Any();
    }

    private static string ResolveTensorRtCachePath(ModelCatalogEntry model)
    {
        var cacheKey = model.YoloMetadata?.TensorRtCacheKey ??
                       model.YoloSegmentationMetadata?.TensorRtCacheKey ??
                       model.Id;
        return Path.Combine(AppContext.BaseDirectory, "trt-cache", cacheKey.Trim());
    }

    private static IReadOnlyList<OrtExecutionProviderKind> BuildDiagnosticProviderOrder(bool includeTensorRt)
    {
        return includeTensorRt
            ? new[] { OrtExecutionProviderKind.TensorRt, OrtExecutionProviderKind.Cuda, OrtExecutionProviderKind.Cpu }
            : new[] { OrtExecutionProviderKind.Cuda, OrtExecutionProviderKind.Cpu };
    }

    private static IReadOnlyList<string> BuildLoadLibraryCandidates(
        DesktopNativeRuntimeLayout layout,
        IReadOnlyList<string> tensorRtRuntimeFiles,
        bool includeTensorRt)
    {
        var candidates = new List<string>
        {
            Path.Combine(layout.OrtNativeDir, "onnxruntime.dll"),
            Path.Combine(layout.OrtNativeDir, "onnxruntime_providers_shared.dll"),
            Path.Combine(layout.OrtNativeDir, "onnxruntime_providers_cuda.dll"),
            Path.Combine(layout.OrtNativeDir, "zlibwapi.dll"),
            Path.Combine(layout.MvsRuntimeX64, "MvCameraControl.dll"),
            Path.Combine(layout.MvsRuntimeX64, "MvUsb3vTL.dll"),
            Path.Combine(layout.MvsRuntimeX64, "MVGigEVisionSDK.dll"),
            Path.Combine(layout.CudaBin, "cudart64_12.dll"),
            Path.Combine(layout.CudaBin, "cublas64_12.dll"),
            Path.Combine(layout.CudaBin, "cublasLt64_12.dll"),
            Path.Combine(layout.CudnnBin, "cudnn64_9.dll"),
            Path.Combine(layout.CudnnBin, "cudnn_ops64_9.dll"),
            Path.Combine(layout.CudnnBin, "cudnn_cnn64_9.dll"),
            Path.Combine(layout.CudnnBin, "cudnn_adv64_9.dll")
        };

        if (includeTensorRt)
        {
            candidates.Add(Path.Combine(layout.OrtNativeDir, "onnxruntime_providers_tensorrt.dll"));
            candidates.AddRange(tensorRtRuntimeFiles);
        }

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void LogFile(List<string> lines, List<DiagnosticsCheckResult> checks, string category, string path, DiagnosticsSeverity missingSeverity = DiagnosticsSeverity.Warning)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!File.Exists(path))
        {
            Append(lines, $"Missing: {path}");
            checks.Add(new DiagnosticsCheckResult(category, Path.GetFileName(path), missingSeverity, $"Missing: {path}"));
            return;
        }

        var info = new FileInfo(path);
        Append(lines, $"Found: {path} | {info.Length} bytes | {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
        checks.Add(new DiagnosticsCheckResult(category, Path.GetFileName(path), DiagnosticsSeverity.Info, $"Found: {path}"));
    }

    private static void LogLoadLibrary(List<string> lines, List<DiagnosticsCheckResult> checks, string path)
    {
        if (!File.Exists(path))
        {
            Append(lines, $"Skip LoadLibrary (missing): {path}");
            checks.Add(new DiagnosticsCheckResult("LoadLibrary", Path.GetFileName(path), DiagnosticsSeverity.Warning, $"Skipped missing file: {path}"));
            return;
        }

        var handle = LoadLibrary(path);
        if (handle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            var guidance = error == 126
                ? " Dependent DLL was not found in the current process search path."
                : string.Empty;
            var severity = IsOrtProviderDll(path) && error == 1114
                ? DiagnosticsSeverity.Info
                : DiagnosticsSeverity.Warning;
            Append(lines, $"LoadLibrary failed ({error}): {path}{guidance}");
            checks.Add(new DiagnosticsCheckResult("LoadLibrary", Path.GetFileName(path), severity, $"LoadLibrary failed ({error}): {path}{guidance}"));
            return;
        }

        Append(lines, $"LoadLibrary ok: {path}");
        checks.Add(new DiagnosticsCheckResult("LoadLibrary", Path.GetFileName(path), DiagnosticsSeverity.Info, $"LoadLibrary ok: {path}"));
        FreeLibrary(handle);
    }

    private static bool IsOrtProviderDll(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.StartsWith("onnxruntime_providers_", StringComparison.OrdinalIgnoreCase);
    }

    private static DiagnosticsCheckResult CreatePathCheck(string category, string name, string path, DiagnosticsSeverity missingSeverity = DiagnosticsSeverity.Warning)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new DiagnosticsCheckResult(category, name, missingSeverity, "Path is empty.");
        }

        var exists = Directory.Exists(path) || File.Exists(path);
        return new DiagnosticsCheckResult(
            category,
            name,
            exists ? DiagnosticsSeverity.Info : missingSeverity,
            exists ? path : $"{path} (missing)");
    }

    private static string FormatProviderOrder(IReadOnlyList<OrtExecutionProviderKind> providerOrder)
    {
        return providerOrder.Count == 0
            ? "<default>"
            : string.Join(" -> ", providerOrder);
    }

    private static IReadOnlyList<string> FindTensorRtRuntimeFiles(DesktopNativeRuntimeLayout layout)
    {
        var patterns = new[]
        {
            "nvinfer*.dll",
            "nvonnxparser*.dll",
            "nvinfer_builder_resource*.dll"
        };

        var files = new List<string>();
        foreach (var directory in layout.EnumerateTensorRtDirectories())
        {
            foreach (var pattern in patterns)
            {
                try
                {
                    files.AddRange(Directory.GetFiles(directory, pattern));
                }
                catch
                {
                }
            }
        }

        return files
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string DescribePath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? "<empty>"
            : Directory.Exists(path) || File.Exists(path)
                ? path
                : $"{path} (missing)";
    }

    private static void Append(List<string> lines, string text)
    {
        lines.Add(text);
    }

    private static EnvironmentVariableSnapshot LogEnvironmentVariable(
        List<string> lines,
        List<DiagnosticsCheckResult> checks,
        string name)
    {
        var snapshot = new EnvironmentVariableSnapshot(
            name,
            Environment.GetEnvironmentVariable(name),
            ReadEnvironmentVariable(name, EnvironmentVariableTarget.User),
            ReadEnvironmentVariable(name, EnvironmentVariableTarget.Machine));
        Append(lines, $"{name} (process): {FormatValue(snapshot.ProcessValue)}");
        Append(lines, $"{name} (user): {FormatValue(snapshot.UserValue)}");
        Append(lines, $"{name} (machine): {FormatValue(snapshot.MachineValue)}");
        checks.Add(new DiagnosticsCheckResult(
            "Environment",
            name,
            DiagnosticsSeverity.Info,
            $"process={FormatValue(snapshot.ProcessValue)}, user={FormatValue(snapshot.UserValue)}, machine={FormatValue(snapshot.MachineValue)}"));
        return snapshot;
    }

    private static IReadOnlyList<string> ReadProcessPathEntries()
    {
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Trim())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void LogPathMatches(
        List<string> lines,
        List<DiagnosticsCheckResult> checks,
        string label,
        IReadOnlyList<string> pathEntries,
        bool required,
        params string[] keywords)
    {
        var matches = pathEntries
            .Where(path => keywords.Any(keyword => path.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Append(lines, $"PATH matches for {label}: {matches.Length}");
        foreach (var match in matches)
        {
            Append(lines, $"  {match}");
        }

        var severity = required && matches.Length == 0 ? DiagnosticsSeverity.Warning : DiagnosticsSeverity.Info;
        var message = matches.Length == 0
            ? "No matching entries."
            : string.Join(" | ", matches);
        checks.Add(new DiagnosticsCheckResult("Environment", $"{label} PATH matches", severity, message));
    }

    private static void LogEnvironmentConsistencyWarnings(
        List<string> lines,
        List<DiagnosticsCheckResult> checks,
        DesktopNativeRuntimeLayout layout,
        bool probesTensorRt,
        EnvironmentVariableSnapshot cudaPath,
        EnvironmentVariableSnapshot tensorRtPath,
        EnvironmentVariableSnapshot tensorRtRoot)
    {
        if (!string.IsNullOrWhiteSpace(cudaPath.MachineValue) &&
            string.IsNullOrWhiteSpace(cudaPath.ProcessValue))
        {
            var message = "CUDA_PATH exists for the machine, but the current process does not see it. Restart the IDE or application process.";
            Append(lines, message);
            checks.Add(new DiagnosticsCheckResult("Environment", "CUDA process refresh", DiagnosticsSeverity.Warning, message));
        }

        if (!probesTensorRt)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(layout.TensorRtRoot))
        {
            var message = "TensorRT root was not resolved from the current environment or common install locations.";
            Append(lines, message);
            checks.Add(new DiagnosticsCheckResult("Environment", "TensorRT discovery", DiagnosticsSeverity.Warning, message));
        }

        var hasPersistedTensorRtSetting =
            !string.IsNullOrWhiteSpace(tensorRtPath.UserValue) ||
            !string.IsNullOrWhiteSpace(tensorRtPath.MachineValue) ||
            !string.IsNullOrWhiteSpace(tensorRtRoot.UserValue) ||
            !string.IsNullOrWhiteSpace(tensorRtRoot.MachineValue);
        var seesTensorRtInProcess =
            !string.IsNullOrWhiteSpace(tensorRtPath.ProcessValue) ||
            !string.IsNullOrWhiteSpace(tensorRtRoot.ProcessValue);
        if (hasPersistedTensorRtSetting && !seesTensorRtInProcess)
        {
            var message = "TensorRT environment variables exist for the user/machine, but the current process has not refreshed them. Restart the IDE or application process.";
            Append(lines, message);
            checks.Add(new DiagnosticsCheckResult("Environment", "TensorRT process refresh", DiagnosticsSeverity.Warning, message));
        }
    }

    private static IReadOnlyList<string> BuildRecommendations(
        IReadOnlyList<DiagnosticsCheckResult> checks,
        DesktopNativeRuntimeLayout layout,
        bool probesTensorRt)
    {
        var recommendations = new List<string>();

        if (probesTensorRt && string.IsNullOrWhiteSpace(layout.TensorRtRoot))
        {
            recommendations.Add("Set TENSORRT_ROOT or TENSORRT_PATH to the TensorRT package root, then restart the application process.");
        }

        if (checks.Any(check =>
                check.Category == "LoadLibrary" &&
                check.Name.Equals("onnxruntime_providers_tensorrt.dll", StringComparison.OrdinalIgnoreCase) &&
                check.Message.Contains("(126)", StringComparison.OrdinalIgnoreCase)))
        {
            recommendations.Add("TensorRT provider DLL was found but its dependent DLLs were not visible. Verify TensorRT lib/bin, CUDA bin, and cuDNN directories are in the current process search path.");
        }

        if (checks.Any(check =>
                check.Category == "ORT" &&
                check.Name.Equals("Provider fallback", StringComparison.OrdinalIgnoreCase)))
        {
            recommendations.Add("Provider fallback occurred. Review the LoadLibrary and Files sections first; they usually identify the missing runtime dependency.");
        }

        if (checks.Any(check =>
                check.Category == "Files" &&
                check.Name.Equals("TensorRT runtime discovery", StringComparison.OrdinalIgnoreCase) &&
                check.Severity != DiagnosticsSeverity.Info))
        {
            recommendations.Add("TensorRT runtime DLLs were not discovered under the resolved directories. Verify that the Windows zip package still contains lib\\nvinfer*.dll and related parser/resource DLLs.");
        }

        var providerFailures = checks
            .Where(check => check.Category == "ORT" && check.Name != "Session Probe" && check.Name != "Selected provider" && check.Name != "Provider init log" && check.Severity == DiagnosticsSeverity.Error)
            .Select(check => check.Name)
            .Distinct()
            .ToArray();
        if (providerFailures.Length > 0)
        {
            recommendations.Add($"Provider initialization failure(s): {string.Join(", ", providerFailures)}. See the provider init log for full exception details.");
        }

        if (recommendations.Count == 0)
        {
            var firstIssue = checks.FirstOrDefault(check => check.Severity != DiagnosticsSeverity.Info);
            if (firstIssue != null)
            {
                recommendations.Add($"{firstIssue.Category}/{firstIssue.Name}: {firstIssue.Message}");
            }
        }

        return recommendations
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ReadEnvironmentVariable(string name, EnvironmentVariableTarget target)
    {
        try
        {
            return Environment.GetEnvironmentVariable(name, target);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<null>" : value;
    }

    private sealed record EnvironmentVariableSnapshot(
        string Name,
        string? ProcessValue,
        string? UserValue,
        string? MachineValue);

    private static void LogProviderFallbackChain(
        List<string> lines,
        List<DiagnosticsCheckResult> checks,
        IReadOnlyList<OrtExecutionProviderKind> providerOrder,
        OrtExecutionProviderKind selectedProvider)
    {
        foreach (var provider in providerOrder)
        {
            if (provider == selectedProvider)
            {
                break;
            }

            Append(lines, $"Provider {provider}: failed before {selectedProvider} was attempted.");
            checks.Add(new DiagnosticsCheckResult(
                "ORT",
                $"Provider {provider}",
                DiagnosticsSeverity.Info,
                $"Provider {provider} failed, fell through to {selectedProvider}."));
        }
    }

    private static void LogProviderFailuresFromException(
        List<string> lines,
        List<DiagnosticsCheckResult> checks,
        Exception exception)
    {
        var message = exception.Message;
        foreach (var line in message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            // Lines like "TensorRt: DllNotFoundException: ..." or "Cuda: OnnxRuntimeException: ..."
            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0)
            {
                continue;
            }

            var providerName = line[..colonIndex].Trim();
            if (!Enum.TryParse<OrtExecutionProviderKind>(providerName, ignoreCase: true, out var provider))
            {
                continue;
            }

            var failureDetail = line[(colonIndex + 1)..].Trim();
            Append(lines, $"Provider {providerName}: FAILED - {failureDetail}");
            checks.Add(new DiagnosticsCheckResult(
                "ORT",
                $"Provider {providerName}",
                DiagnosticsSeverity.Error,
                failureDetail));
        }
    }

    private static void LogRuntimeInitFiltering(
        List<string> lines,
        List<DiagnosticsCheckResult> checks,
        DesktopNativeRuntimeLayout layout)
    {
        var initLogPath = Path.Combine(AppContext.BaseDirectory, "native_runtime_init.log");
        if (!File.Exists(initLogPath))
        {
            Append(lines, "native_runtime_init.log not found — runtime init may not have run yet.");
            return;
        }

        try
        {
            var logContent = File.ReadAllText(initLogPath);

            // Extract removed/conflicting PATH entries
            foreach (var keyword in new[] { "CUDNN", "miniconda", "anaconda", "Win32" })
            {
                var mentions = logContent.Contains(keyword, StringComparison.OrdinalIgnoreCase);
                Append(lines, $"PATH conflict removal ({keyword}): {(mentions ? "present in init log" : "not detected")}");
            }

            var updatedEntryCount = ExtractNumberAfter(logContent, "Updated PATH entries:");
            var originalEntryCount = ExtractNumberAfter(logContent, "Original PATH entries:");
            Append(lines, $"PATH entries: {originalEntryCount} original → {updatedEntryCount} after filtering+prepend");
            Append(lines, $"Init log: {initLogPath}");
        }
        catch
        {
            Append(lines, "Could not parse native_runtime_init.log.");
        }
    }

    private static void LogTensorRtVersion(List<string> lines, string dllPath)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(dllPath);
            var fileName = Path.GetFileName(dllPath);
            if (versionInfo.FileVersion != null || versionInfo.ProductVersion != null)
            {
                Append(lines, $"{fileName}: {versionInfo.FileVersion ?? "?"} (product {versionInfo.ProductVersion ?? "?"})");
            }
        }
        catch
        {
        }
    }

    private static void LogProviderInitLogPath(List<string> lines, List<DiagnosticsCheckResult> checks)
    {
        var path = OrtSessionFactory.ProviderLogPath;
        var exists = File.Exists(path);
        Append(lines, $"Provider init log: {path} ({(exists ? "exists" : "not yet created")})");
        checks.Add(new DiagnosticsCheckResult(
            "ORT",
            "Provider init log",
            DiagnosticsSeverity.Info,
            $"{path}"));
    }

    private static int ExtractNumberAfter(string text, string prefix)
    {
        var index = text.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return -1;
        }

        var after = text[(index + prefix.Length)..].TrimStart();
        var firstSpace = after.IndexOf(' ');
        var numberStr = firstSpace > 0 ? after[..firstSpace] : after;
        return int.TryParse(numberStr, out var value) ? value : -1;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);
}
