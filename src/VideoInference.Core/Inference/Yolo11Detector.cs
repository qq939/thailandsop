using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NewLife.Log;
using OpenCvSharp;

namespace VideoInferenceDemo;

public sealed class Yolo11Detector : IDisposable
{
    private readonly string _modelPath;
    private readonly float _confidenceThreshold;
    private readonly float _nmsThreshold;
    private readonly YoloDetectionMetadata? _metadata;
    private readonly OrtSessionFactoryOptions? _sessionFactoryOptions;
    private string[]? _classNames;
    private InferenceDeviceKind _deviceKind;
    private bool _fallbackAttempted;

    private OrtModelRuntime? _ortRuntime;
    private YoloDetectionPreprocessor? _ortPreprocessor;
    private YoloDetectionPostprocessor? _ortPostprocessor;

    public Yolo11Detector(
        string modelPath,
        InferenceDeviceKind deviceKind,
        float confidenceThreshold,
        float nmsThreshold,
        string[]? classNames,
        YoloDetectionMetadata? metadata = null)
    {
        _modelPath = modelPath;
        _deviceKind = deviceKind;
        _confidenceThreshold = confidenceThreshold;
        _nmsThreshold = nmsThreshold;
        _classNames = classNames;
        _metadata = metadata;
        _sessionFactoryOptions = BuildSessionFactoryOptions(deviceKind, metadata?.TensorRtCacheKey);

        CreateOrtComponents(deviceKind, null);
    }

    public string? ActiveDeviceLabel { get; private set; }
    public float ConfidenceThreshold => _confidenceThreshold;
    public float NmsThreshold => _nmsThreshold;
    public YoloInferenceTiming LastTiming { get; private set; } = YoloInferenceTiming.Empty;

    public void UpdateClasses(string[]? classNames)
    {
        _classNames = classNames;
    }

    public bool TryFallbackToCpu(out string message)
    {
        message = string.Empty;
        if (_fallbackAttempted || _deviceKind == InferenceDeviceKind.Cpu)
        {
            return false;
        }

        _fallbackAttempted = true;
        try
        {
            var provider = _ortRuntime?.CurrentProvider ?? OrtExecutionProviderKind.Cpu;
            if (provider != OrtExecutionProviderKind.Cpu && TrySwapOrtSession(InferenceDeviceKind.Cpu, OrtExecutionProviderKind.Cpu))
            {
                message = "GPU inference failed. Automatically switched to CPU.";
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            message = $"GPU inference failed, and fallback also failed: {ex.Message}";
            return false;
        }
    }

    public YoloDetection[] Predict(Mat image)
    {
        if (image == null || image.Empty())
        {
            return Array.Empty<YoloDetection>();
        }

        return PredictOrt(image);
    }

    public void Dispose()
    {
        _ortRuntime?.Dispose();
    }

    private YoloDetection[] PredictOrt(Mat image)
    {
        if (_ortRuntime == null || _ortPreprocessor == null || _ortPostprocessor == null)
        {
            return Array.Empty<YoloDetection>();
        }

        var totalWatch = Stopwatch.StartNew();
        var preprocessWatch = Stopwatch.StartNew();
        var input = _ortPreprocessor.Preprocess(image, out var transform);
        preprocessWatch.Stop();
        var runWatch = Stopwatch.StartNew();
        using var output = _ortRuntime.Run(input);
        runWatch.Stop();
        var postprocessWatch = Stopwatch.StartNew();
        var result = _ortPostprocessor.Process(output, transform);
        postprocessWatch.Stop();
        totalWatch.Stop();
        LastTiming = new YoloInferenceTiming(
            preprocessWatch.Elapsed.TotalMilliseconds,
            runWatch.Elapsed.TotalMilliseconds,
            postprocessWatch.Elapsed.TotalMilliseconds,
            totalWatch.Elapsed.TotalMilliseconds);
        return result;
    }

    private void CreateOrtComponents(InferenceDeviceKind deviceKind, OrtExecutionProviderKind? forcedProvider)
    {
        _ortRuntime?.Dispose();
        var runtimeOptions = BuildRuntimeOptions(deviceKind);
        _ortRuntime = runtimeOptions == null
            ? new OrtModelRuntime(_modelPath, deviceKind, forcedProvider: forcedProvider)
            : new OrtModelRuntime(_modelPath, runtimeOptions, forcedProvider: forcedProvider);
        _deviceKind = deviceKind;
        ResolveInputSize(_ortRuntime.InputDimensions, out var inputWidth, out var inputHeight);
        _ortPreprocessor = new YoloDetectionPreprocessor(_ortRuntime.InputName, inputWidth, inputHeight);
        _ortPostprocessor = new YoloDetectionPostprocessor(_ortRuntime.OutputName, _confidenceThreshold, _nmsThreshold, _classNames, _metadata);
        ActiveDeviceLabel = _ortRuntime.ActiveDeviceLabel;
    }

    private bool TrySwapOrtSession(InferenceDeviceKind deviceKind, OrtExecutionProviderKind forcedProvider)
    {
        try
        {
            CreateOrtComponents(deviceKind, forcedProvider);
            ActiveDeviceLabel = _ortRuntime?.ActiveDeviceLabel;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private OrtSessionFactoryOptions? BuildRuntimeOptions(InferenceDeviceKind deviceKind)
    {
        if (_sessionFactoryOptions == null)
        {
            return null;
        }

        var options = new OrtSessionFactoryOptions
        {
            DeviceKind = deviceKind,
            ProviderOrder = _sessionFactoryOptions.ProviderOrder,
            NativeLibraryPath = _sessionFactoryOptions.NativeLibraryPath,
            DeviceId = _sessionFactoryOptions.DeviceId,
            TensorRtFp16 = _sessionFactoryOptions.TensorRtFp16,
            TensorRtEngineCache = _sessionFactoryOptions.TensorRtEngineCache,
            TensorRtEngineCachePath = _sessionFactoryOptions.TensorRtEngineCachePath,
            InterOpNumThreads = _sessionFactoryOptions.InterOpNumThreads,
            IntraOpNumThreads = _sessionFactoryOptions.IntraOpNumThreads,
            GraphOptimizationLevel = _sessionFactoryOptions.GraphOptimizationLevel,
            ExecutionMode = _sessionFactoryOptions.ExecutionMode,
            EnableCpuMemArena = _sessionFactoryOptions.EnableCpuMemArena,
            EnableMemoryPattern = _sessionFactoryOptions.EnableMemoryPattern
        };

        if (deviceKind == InferenceDeviceKind.GpuCuda)
        {
            var cacheKey = _metadata?.TensorRtCacheKey;
            if (TryResolveTensorRtCachePath(cacheKey, out var cachePath))
            {
                options.ProviderOrder = new[]
                {
                    OrtExecutionProviderKind.TensorRt,
                    OrtExecutionProviderKind.Cuda,
                    OrtExecutionProviderKind.Cpu
                };
                options.TensorRtFp16 = true;
                options.TensorRtEngineCache = true;
                options.TensorRtEngineCachePath = cachePath;
                XTrace.WriteLine("[Yolo11Detector] TensorRT cache found at {0}, enabled.", cachePath);
            }
            else
            {
                options.ProviderOrder = new[]
                {
                    OrtExecutionProviderKind.Cuda,
                    OrtExecutionProviderKind.Cpu
                };
                XTrace.WriteLine("[Yolo11Detector] TensorRT cache not found for key '{0}', falling back to CUDA.", cacheKey ?? "<null>");
            }
        }

        return options;
    }

    private static OrtSessionFactoryOptions? BuildSessionFactoryOptions(
        InferenceDeviceKind deviceKind,
        string? tensorRtCacheKey)
    {
        if (deviceKind == InferenceDeviceKind.Cpu)
        {
            return new OrtSessionFactoryOptions
            {
                DeviceKind = deviceKind,
                ProviderOrder = new[] { OrtExecutionProviderKind.Cpu }
            };
        }

        if (deviceKind == InferenceDeviceKind.GpuCuda)
        {
            return new OrtSessionFactoryOptions
            {
                DeviceKind = deviceKind
            };
        }

        var cachePath = ResolveTensorRtCachePath(tensorRtCacheKey);
        EnsureTensorRtCacheExists(cachePath);
        return new OrtSessionFactoryOptions
        {
            DeviceKind = deviceKind,
            ProviderOrder = new[] { OrtExecutionProviderKind.TensorRt },
            TensorRtFp16 = true,
            TensorRtEngineCache = true,
            TensorRtEngineCachePath = cachePath
        };
    }

    private static string ResolveTensorRtCachePath(string? tensorRtCacheKey)
    {
        if (string.IsNullOrWhiteSpace(tensorRtCacheKey))
        {
            throw new InvalidOperationException("TensorRT cache key is required for GpuRt.");
        }

        return Path.Combine(AppContext.BaseDirectory, "trt-cache", tensorRtCacheKey.Trim());
    }

    private static bool TryResolveTensorRtCachePath(string? tensorRtCacheKey, out string cachePath)
    {
        cachePath = string.Empty;
        foreach (var candidate in EnumerateTensorRtCacheCandidates(tensorRtCacheKey))
        {
            if (Directory.Exists(candidate) &&
                Directory.EnumerateFiles(candidate, "*", SearchOption.AllDirectories).Any())
            {
                cachePath = candidate;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateTensorRtCacheCandidates(string? tensorRtCacheKey)
    {
        if (!string.IsNullOrWhiteSpace(tensorRtCacheKey))
        {
            yield return Path.Combine(AppContext.BaseDirectory, "trt-cache", tensorRtCacheKey.Trim());
        }
    }

    private static void EnsureTensorRtCacheExists(string cachePath)
    {
        if (!Directory.Exists(cachePath) ||
            !Directory.EnumerateFiles(cachePath, "*", SearchOption.AllDirectories).Any())
        {
            throw new InvalidOperationException(
                $"TensorRT cache is required before using GpuRt. Build cache first: {cachePath}");
        }
    }

    private static void ResolveInputSize(IReadOnlyList<int> dimensions, out int width, out int height)
    {
        width = 640;
        height = 640;
        if (dimensions.Count >= 4)
        {
            if (dimensions[^1] > 0)
            {
                width = dimensions[^1];
            }

            if (dimensions[^2] > 0)
            {
                height = dimensions[^2];
            }
        }
    }
}
