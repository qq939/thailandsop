using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace VideoInferenceDemo;

public static class TensorRtCacheBuilder
{
    public static async Task<TensorRtCacheBuildResult> BuildCacheAsync(
        string modelPath,
        string cacheDirectory,
        int inputWidth,
        int inputHeight,
        int deviceId = 0,
        bool fp16 = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            return TensorRtCacheBuildResult.Failed($"模型文件不存在: {modelPath}");
        }

        if (inputWidth <= 0 || inputHeight <= 0)
        {
            return TensorRtCacheBuildResult.Failed(
                $"model.json 必须声明有效 inputWidth/inputHeight，当前值: {inputWidth}x{inputHeight}");
        }

        return await Task.Run(() =>
        {
            try
            {
                var resolvedCacheDirectory = ResetCacheDirectory(cacheDirectory);

                var options = new OrtSessionFactoryOptions
                {
                    DeviceKind = InferenceDeviceKind.GpuRt,
                    ProviderOrder = new[]
                    {
                        OrtExecutionProviderKind.TensorRt,
                        OrtExecutionProviderKind.Cuda,
                        OrtExecutionProviderKind.Cpu
                    },
                    DeviceId = deviceId,
                    TensorRtFp16 = fp16,
                    TensorRtEngineCache = true,
                    TensorRtEngineCachePath = resolvedCacheDirectory,
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
                };

                var bundle = OrtSessionFactory.Create(modelPath, options);

                try
                {
                    if (bundle.SelectedProvider != OrtExecutionProviderKind.TensorRt)
                    {
                        return TensorRtCacheBuildResult.Failed(
                            $"TensorRT 不可用，回退至 {OrtSessionFactory.DescribeProvider(bundle.SelectedProvider)}。请确认已安装 TensorRT。");
                    }

                    using (var outputs = bundle.Session.Run(CreateWarmupInputs(bundle.Session, inputWidth, inputHeight)))
                    {
                        _ = outputs.Count;
                    }

                    if (!HasCacheFiles(resolvedCacheDirectory))
                    {
                        return TensorRtCacheBuildResult.Failed(
                            $"TensorRT provider 已启动，但没有写入 engine cache 文件: {resolvedCacheDirectory}");
                    }

                    return TensorRtCacheBuildResult.Succeeded(resolvedCacheDirectory);
                }
                finally
                {
                    bundle.Session.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                return TensorRtCacheBuildResult.Failed("构建已取消。");
            }
            catch (Exception ex)
            {
                return TensorRtCacheBuildResult.Failed(
                    $"TensorRT 引擎缓存构建失败: {ex.Message}");
            }
        }, cancellationToken);
    }

    private static string ResetCacheDirectory(string cacheDirectory)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory))
        {
            throw new ArgumentException("TensorRT cache directory is required.", nameof(cacheDirectory));
        }

        var resolved = Path.GetFullPath(cacheDirectory);
        var root = Path.GetPathRoot(resolved);
        if (string.IsNullOrWhiteSpace(root) ||
            string.Equals(
                resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to clear unsafe TensorRT cache directory: {resolved}");
        }

        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }

        Directory.CreateDirectory(resolved);
        return resolved;
    }

    private static IReadOnlyList<NamedOnnxValue> CreateWarmupInputs(
        InferenceSession session,
        int inputWidth,
        int inputHeight)
    {
        return session.InputMetadata
            .Select(input => CreateWarmupInput(input.Key, input.Value, inputWidth, inputHeight))
            .ToArray();
    }

    private static NamedOnnxValue CreateWarmupInput(
        string name,
        NodeMetadata metadata,
        int inputWidth,
        int inputHeight)
    {
        var dimensions = ResolveWarmupDimensions(metadata.Dimensions, inputWidth, inputHeight);
        var elementCount = dimensions.Aggregate(1, (current, dimension) => checked(current * dimension));

        if (metadata.ElementType == typeof(float))
        {
            return NamedOnnxValue.CreateFromTensor(
                name,
                new DenseTensor<float>(new float[elementCount], dimensions));
        }

        if (metadata.ElementType == typeof(double))
        {
            return NamedOnnxValue.CreateFromTensor(
                name,
                new DenseTensor<double>(new double[elementCount], dimensions));
        }

        if (metadata.ElementType == typeof(long))
        {
            return NamedOnnxValue.CreateFromTensor(
                name,
                new DenseTensor<long>(new long[elementCount], dimensions));
        }

        if (metadata.ElementType == typeof(int))
        {
            return NamedOnnxValue.CreateFromTensor(
                name,
                new DenseTensor<int>(new int[elementCount], dimensions));
        }

        if (metadata.ElementType == typeof(byte))
        {
            return NamedOnnxValue.CreateFromTensor(
                name,
                new DenseTensor<byte>(new byte[elementCount], dimensions));
        }

        if (metadata.ElementType == typeof(bool))
        {
            return NamedOnnxValue.CreateFromTensor(
                name,
                new DenseTensor<bool>(new bool[elementCount], dimensions));
        }

        throw new NotSupportedException($"Unsupported warmup input type for '{name}': {metadata.ElementType}");
    }

    private static int[] ResolveWarmupDimensions(
        IReadOnlyList<int> dimensions,
        int inputWidth,
        int inputHeight)
    {
        if (dimensions.Count == 0)
        {
            return new[] { 1 };
        }

        var resolved = new int[dimensions.Count];
        for (var i = 0; i < dimensions.Count; i++)
        {
            if (dimensions[i] > 0)
            {
                resolved[i] = dimensions[i];
                continue;
            }

            resolved[i] = ResolveDynamicDimension(dimensions.Count, i, inputWidth, inputHeight);
        }

        return resolved;
    }

    private static int ResolveDynamicDimension(
        int rank,
        int dimensionIndex,
        int inputWidth,
        int inputHeight)
    {
        if (rank == 4)
        {
            return dimensionIndex switch
            {
                0 => 1,
                1 => throw new InvalidOperationException("model.json/input metadata must provide a static channel dimension for TensorRT cache warmup."),
                2 => inputHeight,
                3 => inputWidth,
                _ => throw new InvalidOperationException($"Unsupported dynamic dimension index {dimensionIndex} for rank {rank}.")
            };
        }

        if (dimensionIndex == 0)
        {
            return 1;
        }

        throw new InvalidOperationException(
            $"Dynamic input dimension requires explicit model metadata. Rank={rank}, index={dimensionIndex}.");
    }

    private static bool HasCacheFiles(string cacheDirectory)
    {
        return Directory.Exists(cacheDirectory) &&
               Directory.EnumerateFiles(cacheDirectory, "*", SearchOption.AllDirectories).Any();
    }
}

public sealed record TensorRtCacheBuildResult(
    bool Success,
    string CacheDirectory,
    string Message)
{
    public static TensorRtCacheBuildResult Succeeded(string cacheDir)
        => new(true, cacheDir, "TensorRT 引擎缓存构建成功。");

    public static TensorRtCacheBuildResult Failed(string message)
        => new(false, string.Empty, message);
}
