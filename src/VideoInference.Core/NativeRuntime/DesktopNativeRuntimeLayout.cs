using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VideoInferenceDemo;

public sealed record DesktopNativeRuntimeLayout(
    string OrtNativeDir,
    string ThirdPartyDir,
    string CameraHikDir,
    string MvsRuntimeX64,
    string CudaBin,
    string CudnnBin,
    string TensorRtRoot,
    string TensorRtBin,
    string TensorRtLib)
{
    public IReadOnlyList<string> EnumerateTensorRtDirectories()
    {
        return new[] { TensorRtBin, TensorRtLib }
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
