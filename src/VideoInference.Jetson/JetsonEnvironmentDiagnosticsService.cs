using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using OpenCvSharp;
using Sdcb.FFmpeg.Formats;

namespace VideoInferenceDemo;

internal sealed class JetsonEnvironmentDiagnosticsService : IEnvironmentDiagnosticsService
{
    private readonly JetsonHostLayout _layout;
    private readonly string? _ortNativeLibraryPath;
    private readonly string? _providerCsv;

    public JetsonEnvironmentDiagnosticsService(
        JetsonHostLayout layout,
        string? ortNativeLibraryPath,
        string? providerCsv)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _ortNativeLibraryPath = ortNativeLibraryPath;
        _providerCsv = providerCsv;
    }

    public EnvironmentDiagnosticsResult Run()
    {
        var lines = new List<string>();
        var checks = new List<DiagnosticsCheckResult>();
        var nowUtc = DateTimeOffset.UtcNow;

        Append(lines, $"Environment diagnostics started at {nowUtc:yyyy-MM-dd HH:mm:ss} UTC");
        Append(lines, $"BaseDirectory: {_layout.BaseDirectory}");
        Append(lines, $"OS: {RuntimeInformation.OSDescription}");
        Append(lines, $"ProcessArchitecture: {RuntimeInformation.ProcessArchitecture}");
        Append(lines, $"Framework: {RuntimeInformation.FrameworkDescription}");

        Append(lines, string.Empty);
        Append(lines, "[Resolved Paths]");
        Append(lines, $"Camera config: {DescribePath(_layout.CameraConfigPath)}");
        Append(lines, $"Model root: {DescribePath(_layout.ModelRootPath)}");
        Append(lines, $"Log directory: {DescribePath(_layout.LogDirectory)}");
        Append(lines, $"ORT native: {DescribeOrtPath(_layout.BaseDirectory, _ortNativeLibraryPath)}");

        checks.Add(CreatePathCheck("Runtime", "Camera config", _layout.CameraConfigPath, isFile: true));
        checks.Add(CreatePathCheck("Runtime", "Model root", _layout.ModelRootPath, isFile: false));
        checks.Add(CreatePathCheck("Runtime", "Log directory", _layout.LogDirectory, isFile: false, allowMissing: true));

        Append(lines, string.Empty);
        Append(lines, "[OpenCV Probe]");
        RunOpenCvProbe(lines, checks);

        Append(lines, string.Empty);
        Append(lines, "[FFmpeg Probe]");
        RunFfmpegProbe(lines, checks);

        Append(lines, string.Empty);
        Append(lines, "[Hik SDK Probe]");
        RunHikSdkProbe(lines, checks);

        Append(lines, string.Empty);
        Append(lines, "[Model Discovery]");
        var models = ModelCatalog.Discover(_layout.ModelRootPath);
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
        var ortSummary = RunOrtSessionProbe(lines, checks, models);

        Directory.CreateDirectory(_layout.LogDirectory);
        File.WriteAllText(_layout.DiagnosticsReportPath, string.Join(Environment.NewLine, lines), Encoding.UTF8);

        var report = new EnvironmentDiagnosticsReport(nowUtc, checks);
        var state = report.Checks.Any(check => check.Severity == DiagnosticsSeverity.Error)
            ? EnvironmentDiagnosticsState.Error
            : report.Checks.Any(check => check.Severity == DiagnosticsSeverity.Warning)
                ? EnvironmentDiagnosticsState.Warning
                : EnvironmentDiagnosticsState.Success;

        return new EnvironmentDiagnosticsResult(
            state,
            ortSummary,
            _layout.DiagnosticsReportPath,
            report);
    }

    private void RunOpenCvProbe(List<string> lines, List<DiagnosticsCheckResult> checks)
    {
        try
        {
            using var mat = new Mat(4, 4, MatType.CV_8UC3, Scalar.Black);
            using var resized = new Mat();
            Cv2.Resize(mat, resized, new Size(2, 2));
            var version = Cv2.GetVersionString();
            Append(lines, $"OpenCV ready. Version={version}");
            checks.Add(new DiagnosticsCheckResult("OpenCV", "Probe", DiagnosticsSeverity.Info, $"OpenCV ready. Version={version}"));
        }
        catch (Exception ex)
        {
            Append(lines, $"OpenCV probe failed:{Environment.NewLine}{ex}");
            checks.Add(new DiagnosticsCheckResult("OpenCV", "Probe", DiagnosticsSeverity.Error, ex.Message));
        }
    }

    private static void RunFfmpegProbe(List<string> lines, List<DiagnosticsCheckResult> checks)
    {
        try
        {
            var format = OutputFormat.Guess(null, "probe.mkv", null);
            if (format == null)
            {
                Append(lines, "FFmpeg probe failed: OutputFormat.Guess returned null for probe.mkv");
                checks.Add(new DiagnosticsCheckResult("FFmpeg", "Probe", DiagnosticsSeverity.Warning, "OutputFormat.Guess returned null."));
                return;
            }

            Append(lines, "FFmpeg ready. OutputFormat.Guess succeeded for probe.mkv.");
            checks.Add(new DiagnosticsCheckResult("FFmpeg", "Probe", DiagnosticsSeverity.Info, "OutputFormat.Guess succeeded for probe.mkv."));
        }
        catch (Exception ex)
        {
            Append(lines, $"FFmpeg probe failed:{Environment.NewLine}{ex}");
            checks.Add(new DiagnosticsCheckResult("FFmpeg", "Probe", DiagnosticsSeverity.Error, ex.Message));
        }
    }

    private static void RunHikSdkProbe(List<string> lines, List<DiagnosticsCheckResult> checks)
    {
        try
        {
            var probe = HikLinuxProbe.Run();
            if (!probe.NativeLibraryLoaded)
            {
                Append(lines, "Hik SDK probe skipped: native library was not loaded.");
                checks.Add(new DiagnosticsCheckResult("Hik SDK", "Probe", DiagnosticsSeverity.Warning, "Native library was not loaded."));
                return;
            }

            if (!string.IsNullOrWhiteSpace(probe.ErrorMessage))
            {
                Append(lines, $"Hik SDK probe failed: {probe.ErrorMessage}");
                checks.Add(new DiagnosticsCheckResult("Hik SDK", "Probe", DiagnosticsSeverity.Warning, probe.ErrorMessage));
                return;
            }

            Append(lines, $"SDK version: {FormatSdkVersion(probe.SdkVersion)}");
            Append(lines, $"Initialize: {FormatNativeStatus(probe.InitializeResult)}");
            Append(lines, $"EnumDevices: {FormatNativeStatus(probe.EnumerateResult)}");
            Append(lines, $"Devices: {probe.Devices.Count}");

            foreach (var device in probe.Devices)
            {
                var transport = device.TransportLayerType == HikLinuxNative.MvGigEDevice ? "GigE" :
                    device.TransportLayerType == HikLinuxNative.MvUsbDevice ? "USB" :
                    $"0x{device.TransportLayerType:X8}";
                var ipSuffix = string.IsNullOrWhiteSpace(device.IpAddress) ? string.Empty : $" ip={device.IpAddress}";
                var accessSuffix = device.IsAccessible ? " accessible" : " inaccessible";
                Append(lines, $" - {transport} | {device.DisplayName} | sn={device.SerialNumber}{ipSuffix}{accessSuffix}");
            }

            if (probe.CreateHandleResult.HasValue)
            {
                Append(lines, $"CreateHandle: {FormatNativeStatus(probe.CreateHandleResult.Value)}");
            }

            if (probe.OpenDeviceResult.HasValue)
            {
                Append(lines, $"OpenDevice: {FormatNativeStatus(probe.OpenDeviceResult.Value)}");
            }

            if (probe.CloseDeviceResult.HasValue)
            {
                Append(lines, $"CloseDevice: {FormatNativeStatus(probe.CloseDeviceResult.Value)}");
            }

            if (probe.DestroyHandleResult.HasValue)
            {
                Append(lines, $"DestroyHandle: {FormatNativeStatus(probe.DestroyHandleResult.Value)}");
            }

            var severity = probe.EnumerateResult != HikLinuxNative.MvOk
                ? DiagnosticsSeverity.Warning
                : DiagnosticsSeverity.Info;
            var message = probe.Devices.Count == 0
                ? "SDK loaded and enumeration succeeded, but no Hik cameras were found."
                : $"SDK loaded and enumerated {probe.Devices.Count} Hik camera(s).";
            checks.Add(new DiagnosticsCheckResult("Hik SDK", "Probe", severity, message));
        }
        catch (Exception ex)
        {
            Append(lines, $"Hik SDK probe failed:{Environment.NewLine}{ex}");
            checks.Add(new DiagnosticsCheckResult("Hik SDK", "Probe", DiagnosticsSeverity.Warning, ex.Message));
        }
    }

    private string RunOrtSessionProbe(
        List<string> lines,
        List<DiagnosticsCheckResult> checks,
        IReadOnlyList<ModelCatalogEntry> models)
    {
        var probeModel = models.FirstOrDefault(model =>
            Path.GetExtension(model.ModelPath).Equals(".onnx", StringComparison.OrdinalIgnoreCase));
        if (probeModel == null)
        {
            Append(lines, "Skipped: no ONNX model bundle was found.");
            checks.Add(new DiagnosticsCheckResult("ORT", "Session Probe", DiagnosticsSeverity.Warning, "Skipped: no ONNX model bundle was found."));
            return "诊断完成，但没有可用于测试的 ONNX 模型。";
        }

        Append(lines, $"Probe model: {probeModel.DisplayName} ({probeModel.ModelPath})");
        try
        {
            var bundle = OrtSessionFactory.Create(
                probeModel.ModelPath,
                new OrtSessionFactoryOptions
                {
                    DeviceKind = InferenceDeviceKind.GpuCuda,
                    ProviderOrder = ResolveProviderOrder(),
                    NativeLibraryPath = _ortNativeLibraryPath
                });

            using (bundle.Session)
            {
                Append(lines, $"Selected provider: {bundle.SelectedProvider}");
                checks.Add(new DiagnosticsCheckResult(
                    "ORT",
                    "Session Probe",
                    bundle.SelectedProvider == OrtExecutionProviderKind.Cpu ? DiagnosticsSeverity.Warning : DiagnosticsSeverity.Info,
                    $"Selected provider: {bundle.SelectedProvider}"));

                return bundle.SelectedProvider == OrtExecutionProviderKind.Cpu
                    ? "诊断完成，但推理当前回退到了 CPU。"
                    : $"诊断完成，当前可用 provider: {bundle.SelectedProvider}。";
            }
        }
        catch (Exception ex)
        {
            Append(lines, $"ORT session probe failed:{Environment.NewLine}{ex}");
            checks.Add(new DiagnosticsCheckResult("ORT", "Session Probe", DiagnosticsSeverity.Error, ex.Message));
            return $"诊断完成，但 ORT 初始化失败：{ex.Message}";
        }
    }

    private IReadOnlyList<OrtExecutionProviderKind> ResolveProviderOrder()
    {
        var parsed = OrtExecutionProviderParser.ParseCsv(_providerCsv);
        if (parsed.Count > 0)
        {
            return parsed;
        }

        return new[]
        {
            OrtExecutionProviderKind.TensorRt,
            OrtExecutionProviderKind.Cuda,
            OrtExecutionProviderKind.Cpu
        };
    }

    private static DiagnosticsCheckResult CreatePathCheck(string category, string name, string path, bool isFile, bool allowMissing = false)
    {
        var exists = isFile ? File.Exists(path) : Directory.Exists(path);
        var severity = exists ? DiagnosticsSeverity.Info : allowMissing ? DiagnosticsSeverity.Warning : DiagnosticsSeverity.Error;
        var message = exists ? path : $"{path} (missing)";
        return new DiagnosticsCheckResult(category, name, severity, message);
    }

    private static string DescribePath(string path)
    {
        return File.Exists(path) || Directory.Exists(path)
            ? path
            : $"{path} (missing)";
    }

    private static string DescribeOrtPath(string baseDirectory, string? ortNativeLibraryPath)
    {
        if (string.IsNullOrWhiteSpace(ortNativeLibraryPath))
        {
            return $"{Path.Combine(baseDirectory, "libonnxruntime.so")} (auto)";
        }

        return Path.IsPathRooted(ortNativeLibraryPath)
            ? ortNativeLibraryPath
            : Path.Combine(baseDirectory, ortNativeLibraryPath);
    }

    private static string FormatNativeStatus(int value)
    {
        return value == HikLinuxNative.MvOk
            ? "MV_OK"
            : $"0x{unchecked((uint)value):X8}";
    }

    private static string FormatSdkVersion(uint? version)
    {
        if (!version.HasValue)
        {
            return "unknown";
        }

        var value = version.Value;
        var major = (value >> 24) & 0xFF;
        var minor = (value >> 16) & 0xFF;
        var patch = (value >> 8) & 0xFF;
        var build = value & 0xFF;
        return $"{major}.{minor}.{patch}.{build} (0x{value:X8})";
    }

    private static void Append(List<string> lines, string text) => lines.Add(text);
}
