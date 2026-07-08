using System;
using System.Collections.Generic;

namespace VideoInferenceDemo;

public sealed class OrtRuntimeEnvironmentOptions
{
    public string? NativeLibraryPath { get; set; }
    public IReadOnlyList<OrtExecutionProviderKind>? GpuProviderOrder { get; set; }
    public int DeviceId { get; set; }
    public bool TensorRtFp16 { get; set; }
    public bool TensorRtEngineCache { get; set; }
    public string? TensorRtEngineCachePath { get; set; }

    internal OrtRuntimeEnvironmentOptions Clone()
    {
        return new OrtRuntimeEnvironmentOptions
        {
            NativeLibraryPath = NativeLibraryPath,
            GpuProviderOrder = GpuProviderOrder is null ? null : new List<OrtExecutionProviderKind>(GpuProviderOrder),
            DeviceId = DeviceId,
            TensorRtFp16 = TensorRtFp16,
            TensorRtEngineCache = TensorRtEngineCache,
            TensorRtEngineCachePath = TensorRtEngineCachePath
        };
    }
}

public static class OrtRuntimeEnvironment
{
    private static readonly object SyncRoot = new();
    private static OrtRuntimeEnvironmentOptions _current = new();

    public static void Configure(OrtRuntimeEnvironmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var clone = options.Clone();
        lock (SyncRoot)
        {
            _current = clone;
        }

        OrtNativeLibraryLoader.TryPreload(clone.NativeLibraryPath);
    }

    public static OrtRuntimeEnvironmentOptions Snapshot()
    {
        lock (SyncRoot)
        {
            return _current.Clone();
        }
    }
}
