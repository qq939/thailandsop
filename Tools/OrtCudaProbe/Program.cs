using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

internal static class Program
{
    private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;
    private const uint LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400;
    private const uint SEM_FAILCRITICALERRORS = 0x0001;
    private const uint SEM_NOGPFAULTERRORBOX = 0x0002;
    private const uint SEM_NOOPENFILEERRORBOX = 0x8000;

    private static int Main(string[] args)
    {
        var argMap = ParseArgs(args);
        var isChild = argMap.ContainsKey("child");

        var appDir = GetArg(argMap, "app-dir");
        if (string.IsNullOrWhiteSpace(appDir))
        {
            var candidate = Path.Combine(Directory.GetCurrentDirectory(), "bin", "Debug", "net8.0-windows");
            appDir = Directory.Exists(candidate) ? candidate : Directory.GetCurrentDirectory();
        }

        var ortNativeDir = GetArg(argMap, "ort-native");
        if (string.IsNullOrWhiteSpace(ortNativeDir))
        {
            ortNativeDir = Path.Combine(appDir, "runtimes", "win-x64", "native");
        }

        var thirdPartyDir = GetArg(argMap, "third-party");
        if (string.IsNullOrWhiteSpace(thirdPartyDir))
        {
            thirdPartyDir = Path.Combine(appDir, "ThirdParty");
        }

        var cudaBin = GetArg(argMap, "cuda-bin");
        if (string.IsNullOrWhiteSpace(cudaBin))
        {
            var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH") ?? string.Empty;
            cudaBin = string.IsNullOrWhiteSpace(cudaPath) ? string.Empty : Path.Combine(cudaPath, "bin");
        }

        var logPath = GetArg(argMap, "log");
        if (string.IsNullOrWhiteSpace(logPath))
        {
            var logDir = Directory.Exists(appDir) ? appDir : Directory.GetCurrentDirectory();
            logPath = Path.Combine(logDir, "ort_cuda_probe.log");
        }

        using var log = CreateLogger(logPath);
        var logger = new Action<string>(msg =>
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {msg}";
            log.WriteLine(line);
            Console.WriteLine(line);
        });

        SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);

        ConfigureDllSearch(logger, ortNativeDir, thirdPartyDir, cudaBin);

        if (isChild)
        {
            return ChildLoadProvider(logger, ortNativeDir);
        }

        LogEnvironment(logger, appDir, ortNativeDir, thirdPartyDir, cudaBin);

        Preload(logger, ortNativeDir, thirdPartyDir, cudaBin);
        TryPrintCudaVersions(logger);

        var cleanPath = argMap.TryGetValue("clean-path", out var cleanVal)
                        && cleanVal.Equals("true", StringComparison.OrdinalIgnoreCase);
        var childExit = RunChild(logger, argMap, appDir, ortNativeDir, thirdPartyDir, cudaBin, logPath, cleanPath);
        logger($"Child exit code: 0x{childExit:X8}");
        return childExit == 0 ? 0 : 2;
    }

    private static StreamWriter CreateLogger(string logPath)
    {
        var dir = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var stream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        return new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
    }

    private static void LogEnvironment(Action<string> logger, string appDir, string ortNativeDir, string thirdPartyDir, string cudaBin)
    {
        logger("=== ORT CUDA PROBE ===");
        logger($"AppDir: {appDir}");
        logger($"OrtNativeDir: {ortNativeDir} (exists={Directory.Exists(ortNativeDir)})");
        logger($"ThirdPartyDir: {thirdPartyDir} (exists={Directory.Exists(thirdPartyDir)})");
        logger($"CudaBin: {cudaBin} (exists={Directory.Exists(cudaBin)})");
        logger($"OS: {RuntimeInformation.OSDescription}");
        logger($"ProcessArch: {RuntimeInformation.ProcessArchitecture} | Is64BitProcess: {Environment.Is64BitProcess}");
        logger($".NET: {RuntimeInformation.FrameworkDescription}");
        logger($"CUDA_PATH: {Environment.GetEnvironmentVariable("CUDA_PATH")}");
        logger($"CUDA_PATH_V11_8: {Environment.GetEnvironmentVariable("CUDA_PATH_V11_8")}");

        var pathHints = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.IndexOf("cuda", StringComparison.OrdinalIgnoreCase) >= 0
                        || p.IndexOf("cudnn", StringComparison.OrdinalIgnoreCase) >= 0
                        || p.IndexOf("tensorrt", StringComparison.OrdinalIgnoreCase) >= 0
                        || p.IndexOf("conda", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();
        if (pathHints.Length > 0)
        {
            logger("PATH (filtered):");
            foreach (var entry in pathHints)
            {
                logger($"  {entry}");
            }
        }

        foreach (var name in new[]
                 {
                     "cudart64_110.dll", "cublas64_11.dll", "cublasLt64_11.dll",
                     "cufft64_10.dll", "cudnn64_8.dll", "cudnn_ops_infer64_8.dll",
                     "cudnn_cnn_infer64_8.dll"
                 })
        {
            var path = FindOnPath(name);
            logger($"where {name}: {(string.IsNullOrWhiteSpace(path) ? "NOT FOUND" : path)}");
        }
    }

    private static void Preload(Action<string> logger, string ortNativeDir, string thirdPartyDir, string cudaBin)
    {
        var candidates = new List<string>
        {
            Path.Combine(ortNativeDir, "onnxruntime.dll"),
            Path.Combine(ortNativeDir, "onnxruntime_providers_shared.dll"),
            Path.Combine(ortNativeDir, "zlibwapi.dll"),
            Path.Combine(thirdPartyDir, "zlibwapi.dll"),
            Path.Combine(cudaBin, "cudart64_110.dll"),
            Path.Combine(cudaBin, "cublas64_11.dll"),
            Path.Combine(cudaBin, "cublasLt64_11.dll"),
            Path.Combine(cudaBin, "cufft64_10.dll"),
            Path.Combine(cudaBin, "cudnn64_8.dll"),
            Path.Combine(cudaBin, "cudnn_ops_infer64_8.dll"),
            Path.Combine(cudaBin, "cudnn_cnn_infer64_8.dll"),
            Path.Combine(cudaBin, "nvrtc64_112_0.dll"),
            Path.Combine(cudaBin, "nvrtc-builtins64_118.dll"),
            "nvcuda.dll"
        };

        logger("=== Preload ===");
        foreach (var dll in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            TryLoad(logger, dll);
        }
    }

    private static void TryPrintCudaVersions(Action<string> logger)
    {
        logger("=== CUDA Versions ===");
        try
        {
            var err = CudaRuntimeGetVersion(out var runtimeVersion);
            logger($"cudaRuntimeGetVersion: {runtimeVersion} (err={err})");
        }
        catch (Exception ex)
        {
            logger($"cudaRuntimeGetVersion failed: {ex.Message}");
        }

        try
        {
            var err = CudaDriverGetVersion(out var driverVersion);
            logger($"cudaDriverGetVersion: {driverVersion} (err={err})");
        }
        catch (Exception ex)
        {
            logger($"cudaDriverGetVersion failed: {ex.Message}");
        }

        try
        {
            var version = CudnnGetVersion();
            logger($"cudnnGetVersion: {version}");
        }
        catch (Exception ex)
        {
            logger($"cudnnGetVersion failed: {ex.Message}");
        }

        try
        {
            var err = CudaGetDeviceCount(out var count);
            logger($"cudaGetDeviceCount: {count} (err={err})");
        }
        catch (Exception ex)
        {
            logger($"cudaGetDeviceCount failed: {ex.Message}");
        }

        try
        {
            var status = CudnnCreate(out var handle);
            logger($"cudnnCreate: {(int)status} ({status})");
            if (status == CudnnStatus.Success && handle != IntPtr.Zero)
            {
                _ = CudnnDestroy(handle);
            }
        }
        catch (Exception ex)
        {
            logger($"cudnnCreate failed: {ex.Message}");
        }
    }

    private static int RunChild(
        Action<string> logger,
        Dictionary<string, string> args,
        string appDir,
        string ortNativeDir,
        string thirdPartyDir,
        string cudaBin,
        string logPath,
        bool cleanPath)
    {
        var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(exe))
        {
            logger("Cannot locate current executable for child process.");
            return 3;
        }

        var childArgs = new List<string>
        {
            "--child",
            $"--app-dir=\"{appDir}\"",
            $"--ort-native=\"{ortNativeDir}\"",
            $"--third-party=\"{thirdPartyDir}\"",
            $"--cuda-bin=\"{cudaBin}\"",
            $"--log=\"{logPath}\""
        };

        var psi = new ProcessStartInfo(exe, string.Join(" ", childArgs))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (cleanPath)
        {
            var minimalPath = string.Join(";", new[]
            {
                ortNativeDir,
                thirdPartyDir,
                cudaBin,
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Wbem")
            }.Where(p => !string.IsNullOrWhiteSpace(p)));
            psi.Environment["PATH"] = minimalPath;
            psi.Environment["CUDA_PATH"] = Path.GetDirectoryName(cudaBin) ?? string.Empty;
            psi.Environment["CUDA_PATH_V11_8"] = Path.GetDirectoryName(cudaBin) ?? string.Empty;
            logger($"Child PATH cleaned: {minimalPath}");
        }

        logger("=== Child start ===");
        using var proc = Process.Start(psi);
        if (proc == null)
        {
            logger("Failed to start child process.");
            return 4;
        }

        proc.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                logger($"[child] {e.Data}");
            }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                logger($"[child-err] {e.Data}");
            }
        };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();
        logger("=== Child end ===");
        return proc.ExitCode;
    }

    private static int ChildLoadProvider(Action<string> logger, string ortNativeDir)
    {
        var provider = Path.Combine(ortNativeDir, "onnxruntime_providers_cuda.dll");
        logger($"Child loading: {provider}");
        if (!File.Exists(provider))
        {
            logger("Provider not found.");
            return 5;
        }

        var handle = LoadLibrary(provider);
        if (handle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            logger($"LoadLibrary failed ({error}) for {provider}");
            return error == 0 ? 6 : error;
        }

        logger("LoadLibrary ok for onnxruntime_providers_cuda.dll");
        FreeLibrary(handle);
        return 0;
    }

    private static void TryLoad(Action<string> logger, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!File.Exists(path))
        {
            logger($"Missing: {path}");
            return;
        }

        var handle = LoadLibrary(path);
        if (handle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            logger($"LoadLibrary failed ({error}) for {path}");
            return;
        }

        logger($"LoadLibrary ok for {path}");
        FreeLibrary(handle);
    }

    private static string? FindOnPath(string fileName)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in paths)
        {
            try
            {
                var candidate = Path.Combine(p.Trim(), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }
        return null;
    }

    private static void ConfigureDllSearch(Action<string> logger, string ortNativeDir, string thirdPartyDir, string cudaBin)
    {
        var ok = SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LOAD_LIBRARY_SEARCH_USER_DIRS);
        if (!ok)
        {
            logger($"SetDefaultDllDirectories failed: {Marshal.GetLastWin32Error()}");
        }
        else
        {
            logger("SetDefaultDllDirectories ok");
        }

        if (Directory.Exists(ortNativeDir))
        {
            _ = AddDllDirectory(ortNativeDir);
        }
        if (Directory.Exists(thirdPartyDir))
        {
            _ = AddDllDirectory(thirdPartyDir);
        }
        if (!string.IsNullOrWhiteSpace(cudaBin) && Directory.Exists(cudaBin))
        {
            _ = AddDllDirectory(cudaBin);
        }
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in args)
        {
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var trimmed = arg[2..];
            var split = trimmed.Split('=', 2);
            var key = split[0].Trim();
            var value = split.Length > 1 ? split[1].Trim().Trim('"') : "true";
            map[key] = value;
        }
        return map;
    }

    private static string? GetArg(Dictionary<string, string> args, string key)
        => args.TryGetValue(key, out var value) ? value : null;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr AddDllDirectory(string newDirectory);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint uMode);

    [DllImport("cudart64_110.dll", EntryPoint = "cudaRuntimeGetVersion")]
    private static extern int CudaRuntimeGetVersion(out int version);

    [DllImport("cudart64_110.dll", EntryPoint = "cudaDriverGetVersion")]
    private static extern int CudaDriverGetVersion(out int version);

    [DllImport("cudnn64_8.dll", EntryPoint = "cudnnGetVersion")]
    private static extern ulong CudnnGetVersion();

    [DllImport("cudart64_110.dll", EntryPoint = "cudaGetDeviceCount")]
    private static extern int CudaGetDeviceCount(out int count);

    private enum CudnnStatus
    {
        Success = 0,
        NotInitialized = 1,
        AllocFailed = 2,
        BadParam = 3,
        InternalError = 4,
        InvalidValue = 5,
        ArchMismatch = 6,
        MappingError = 7,
        ExecutionFailed = 8,
        NotSupported = 9,
        LicenseError = 10
    }

    [DllImport("cudnn64_8.dll", EntryPoint = "cudnnCreate")]
    private static extern CudnnStatus CudnnCreate(out IntPtr handle);

    [DllImport("cudnn64_8.dll", EntryPoint = "cudnnDestroy")]
    private static extern CudnnStatus CudnnDestroy(IntPtr handle);
}
