using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;

namespace VideoInferenceDemo;

public sealed class OrtSessionFactoryOptions
{
    public InferenceDeviceKind DeviceKind { get; set; } = InferenceDeviceKind.Cpu;
    public IReadOnlyList<OrtExecutionProviderKind>? ProviderOrder { get; set; }
    public string? NativeLibraryPath { get; set; }
    public int DeviceId { get; set; }
    public bool TensorRtFp16 { get; set; }
    public bool TensorRtEngineCache { get; set; }
    public string? TensorRtEngineCachePath { get; set; }
    public int InterOpNumThreads { get; set; } = 1;
    public int IntraOpNumThreads { get; set; } = 1;
    public GraphOptimizationLevel GraphOptimizationLevel { get; set; } = GraphOptimizationLevel.ORT_ENABLE_ALL;
    public ExecutionMode ExecutionMode { get; set; } = ExecutionMode.ORT_SEQUENTIAL;
    public bool EnableCpuMemArena { get; set; } = true;
    public bool EnableMemoryPattern { get; set; } = true;

    internal void ApplyEnvironmentDefaults()
    {
        var env = OrtRuntimeEnvironment.Snapshot();
        NativeLibraryPath ??= env.NativeLibraryPath;
        if ((ProviderOrder == null || ProviderOrder.Count == 0) &&
            env.GpuProviderOrder is { Count: > 0 } &&
            DeviceKind != InferenceDeviceKind.Cpu)
        {
            ProviderOrder = env.GpuProviderOrder;
        }

        if (DeviceId == 0 && env.DeviceId != 0)
        {
            DeviceId = env.DeviceId;
        }

        if (!TensorRtFp16 && env.TensorRtFp16)
        {
            TensorRtFp16 = true;
        }

        if (!TensorRtEngineCache && env.TensorRtEngineCache)
        {
            TensorRtEngineCache = true;
        }

        TensorRtEngineCachePath ??= env.TensorRtEngineCachePath;
    }
}

public readonly struct OrtSessionBundle
{
    public OrtSessionBundle(InferenceSession session, OrtExecutionProviderKind selectedProvider)
    {
        Session = session;
        SelectedProvider = selectedProvider;
    }

    public InferenceSession Session { get; }
    public OrtExecutionProviderKind SelectedProvider { get; }
}

public static class OrtSessionFactory
{
    private static readonly object TensorRtSessionInitLock = new();

    /// <summary>ORT 图优化缓存根目录。</summary>
    private static readonly string OrtCacheDir = Path.Combine(AppContext.BaseDirectory, "ort-cache");

    public static string ProviderLogPath { get; } = Path.Combine(AppContext.BaseDirectory, "ort_provider_init.log");

    /// <summary>
    /// 生成 ORT 图优化缓存文件路径。缓存键包含模型时间戳和 provider，
    /// 模型更新或 provider 切换时自动失效。
    /// </summary>
    internal static string? GetOptimizedModelPath(string modelPath, OrtExecutionProviderKind provider, OrtSessionFactoryOptions options)
    {
        if (provider == OrtExecutionProviderKind.TensorRt ||
            options.GraphOptimizationLevel == GraphOptimizationLevel.ORT_DISABLE_ALL)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(OrtCacheDir);
            var modelName = Path.GetFileNameWithoutExtension(modelPath);
            var stamp = File.GetLastWriteTimeUtc(modelPath).Ticks;

            return Path.Combine(OrtCacheDir,
                $"{modelName}_{provider}_opt{(int)options.GraphOptimizationLevel}_{stamp}.onnx");
        }
        catch
        {
            return null;
        }
    }

    public static OrtSessionBundle Create(string modelPath, OrtSessionFactoryOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            throw new FileNotFoundException("ONNX model not found.", modelPath);
        }

        options ??= new OrtSessionFactoryOptions();
        options.ApplyEnvironmentDefaults();
        OrtNativeLibraryLoader.TryPreload(options.NativeLibraryPath);

        Exception? lastError = null;
        var failures = new List<string>();
        foreach (var provider in ResolveProviderOrder(options))
        {
            try
            {
                LogProviderEvent($"Trying provider {provider} for model '{modelPath}'.");
                var started = Stopwatch.StartNew();
                var session = CreateInferenceSession(modelPath, provider, options);
                started.Stop();
                LogProviderEvent($"Provider {provider} initialized successfully for model '{modelPath}' in {started.Elapsed.TotalMilliseconds:F0} ms.");
                return new OrtSessionBundle(session, provider);
            }
            catch (Exception ex) when (IsRecoverableProviderFailure(ex))
            {
                lastError = ex;
                var failure = BuildProviderFailureMessage(provider, ex);
                failures.Add(failure);
                LogProviderEvent($"Provider {provider} failed for model '{modelPath}'.{Environment.NewLine}{failure}{Environment.NewLine}{ex}");
            }
        }

        var message = failures.Count == 0
            ? "Failed to create an ONNX Runtime session."
            : "Failed to create an ONNX Runtime session with any requested provider." + Environment.NewLine +
              string.Join(Environment.NewLine, failures);
        throw new InvalidOperationException(message, lastError);
    }

    private static void LogProviderEvent(string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
            File.AppendAllText(ProviderLogPath, line);
        }
        catch
        {
        }
    }

    private static InferenceSession CreateInferenceSession(
        string modelPath,
        OrtExecutionProviderKind provider,
        OrtSessionFactoryOptions options)
    {
        using var sessionOptions = CreateSessionOptions(provider, options);

        // 持久化 ORT 图优化结果，后续加载跳过 ~2-4s 的图优化阶段
        var optimizedPath = GetOptimizedModelPath(modelPath, provider, options);
        if (optimizedPath != null)
        {
            sessionOptions.OptimizedModelFilePath = optimizedPath;
        }

        if (provider != OrtExecutionProviderKind.TensorRt)
        {
            return new InferenceSession(modelPath, sessionOptions);
        }

        lock (TensorRtSessionInitLock)
        {
            return new InferenceSession(modelPath, sessionOptions);
        }
    }

    public static IReadOnlyList<OrtExecutionProviderKind> ResolveProviderOrder(OrtSessionFactoryOptions options)
    {
        options.ApplyEnvironmentDefaults();

        if (options.DeviceKind == InferenceDeviceKind.Cpu)
        {
            return new[] { OrtExecutionProviderKind.Cpu };
        }

        // 如果显式设置了 ProviderOrder（来自环境配置或其他来源），则检查是否需要注入 fallback
        if (options.ProviderOrder is { Count: > 0 })
        {
            return EnsureProviderFallback(options);
        }

        // 根据设备类型确定 provider 顺序
        if (options.DeviceKind == InferenceDeviceKind.GpuRt)
        {
            // 优先使用 TensorRT 缓存，如果不存在则回退到 ONNX/CPU
            var providers = new List<OrtExecutionProviderKind>();

            if (TensorRtCacheExists(options))
            {
                providers.Add(OrtExecutionProviderKind.TensorRt);
            }

            // 始终添加 CUDA 和 CPU 作为备选
            providers.Add(OrtExecutionProviderKind.Cuda);
            providers.Add(OrtExecutionProviderKind.Cpu);

            return providers;
        }

        if (options.DeviceKind == InferenceDeviceKind.GpuCuda)
        {
            return new[]
            {
                OrtExecutionProviderKind.Cuda,
                OrtExecutionProviderKind.Cpu
            };
        }

        return new[]
        {
            OrtExecutionProviderKind.Cuda,
            OrtExecutionProviderKind.Cpu
        };
    }

    /// <summary>
    /// 确保 provider 列表中有 CPU 作为最后的 fallback。
    /// 如果列表中的 provider 都不是 TensorRT 缓存或 CUDA，尝试添加 TensorRT（如果有缓存）。
    /// </summary>
    private static IReadOnlyList<OrtExecutionProviderKind> EnsureProviderFallback(OrtSessionFactoryOptions options)
    {
        var original = options.ProviderOrder!;
        var result = new List<OrtExecutionProviderKind>(original);

        // 检查是否已经有 CPU fallback
        bool hasCpu = original.Contains(OrtExecutionProviderKind.Cpu);
        bool hasTensorRt = original.Contains(OrtExecutionProviderKind.TensorRt);
        bool hasCuda = original.Contains(OrtExecutionProviderKind.Cuda);

        // 如果有 TensorRT 缓存但 provider 列表中没有，插入到最前面
        if (TensorRtCacheExists(options) && !hasTensorRt)
        {
            result.Insert(0, OrtExecutionProviderKind.TensorRt);
        }

        // 如果没有 CPU，添加到末尾作为最终 fallback
        if (!hasCpu)
        {
            result.Add(OrtExecutionProviderKind.Cpu);
        }

        return result;
    }

    /// <summary>
    /// 检查指定模型的 TensorRT 缓存是否存在。
    /// </summary>
    public static bool TensorRtCacheExists(OrtSessionFactoryOptions options, string? modelPath = null)
    {
        if (string.IsNullOrWhiteSpace(options.TensorRtEngineCachePath))
        {
            return false;
        }

        try
        {
            var cacheDir = options.TensorRtEngineCachePath;
            if (!string.IsNullOrWhiteSpace(modelPath))
            {
                // 如果提供了模型路径，使用模型特定的子目录
                var modelName = Path.GetFileNameWithoutExtension(modelPath);
                cacheDir = Path.Combine(cacheDir, modelName);
            }

            if (!Directory.Exists(cacheDir))
            {
                return false;
            }

            // 检查缓存目录中是否有 .engine 或 .trt 文件
            var files = Directory.GetFiles(cacheDir, "*.engine");
            if (files.Length == 0)
            {
                files = Directory.GetFiles(cacheDir, "*.trt");
            }

            return files.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static string DescribeProvider(OrtExecutionProviderKind provider)
    {
        return provider switch
        {
            OrtExecutionProviderKind.Cuda => "OnnxRuntime / CUDA",
            OrtExecutionProviderKind.TensorRt => "OnnxRuntime / TensorRT",
            _ => "OnnxRuntime / CPU"
        };
    }

    private static SessionOptions CreateSessionOptions(OrtExecutionProviderKind provider, OrtSessionFactoryOptions options)
    {
        var sessionOptions = new SessionOptions
        {
            InterOpNumThreads = options.InterOpNumThreads,
            IntraOpNumThreads = options.IntraOpNumThreads,
            GraphOptimizationLevel = options.GraphOptimizationLevel,
            ExecutionMode = options.ExecutionMode,
            EnableCpuMemArena = options.EnableCpuMemArena,
            EnableMemoryPattern = options.EnableMemoryPattern
        };

        AppendExecutionProvider(sessionOptions, provider, options);
        return sessionOptions;
    }

    private static void AppendExecutionProvider(
        SessionOptions sessionOptions,
        OrtExecutionProviderKind provider,
        OrtSessionFactoryOptions options)
    {
        switch (provider)
        {
            case OrtExecutionProviderKind.Cpu:
                return;
            case OrtExecutionProviderKind.Cuda:
            {
                using var cudaOptions = new OrtCUDAProviderOptions();
                cudaOptions.UpdateOptions(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["device_id"] = options.DeviceId.ToString(CultureInfo.InvariantCulture)
                });
                sessionOptions.AppendExecutionProvider_CUDA(cudaOptions);
                return;
            }
            case OrtExecutionProviderKind.TensorRt:
            {
                using var tensorRtOptions = new OrtTensorRTProviderOptions();
                var providerOptions = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["device_id"] = options.DeviceId.ToString(CultureInfo.InvariantCulture),
                    ["trt_fp16_enable"] = options.TensorRtFp16 ? "1" : "0",
                    ["trt_engine_cache_enable"] = options.TensorRtEngineCache ? "1" : "0"
                };

                if (!string.IsNullOrWhiteSpace(options.TensorRtEngineCachePath))
                {
                    Directory.CreateDirectory(options.TensorRtEngineCachePath);
                    providerOptions["trt_engine_cache_path"] = Path.GetFullPath(options.TensorRtEngineCachePath);
                }
                else if (options.TensorRtEngineCache)
                {
                    var defaultPath = Path.Combine(AppContext.BaseDirectory, "trt-cache");
                    Directory.CreateDirectory(defaultPath);
                    providerOptions["trt_engine_cache_path"] = defaultPath;
                }

                tensorRtOptions.UpdateOptions(providerOptions);
                sessionOptions.AppendExecutionProvider_Tensorrt(tensorRtOptions);
                return;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
        }
    }

    private static bool IsRecoverableProviderFailure(Exception ex)
    {
        return ex is OnnxRuntimeException
               || ex is DllNotFoundException
               || ex is TypeInitializationException
               || ex is BadImageFormatException
               || ex is InvalidOperationException
               || ex is EntryPointNotFoundException;
    }

    private static string BuildProviderFailureMessage(OrtExecutionProviderKind provider, Exception ex)
    {
        var message = $"{provider}: {ex.GetType().Name}: {ex.Message}";
        if (provider == OrtExecutionProviderKind.TensorRt &&
            ex.Message.Contains("compiled nodes", StringComparison.OrdinalIgnoreCase) &&
            ex.Message.Contains("serialize", StringComparison.OrdinalIgnoreCase))
        {
            message += " TensorRT provider should not use OptimizedModelFilePath; rebuild with the fixed runtime.";
        }

        return message;
    }
}
