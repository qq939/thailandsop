using System.IO;
using System.Linq;

namespace VideoInferenceDemo;

public static class DesktopNativeRuntimePathResolver
{
    public static DesktopNativeRuntimeLayout Resolve(string baseDirectory)
    {
        var ortNativeDir = Path.Combine(baseDirectory, "runtimes", "win-x64", "native");
        var thirdPartyDir = Path.Combine(baseDirectory, "ThirdParty");
        var cameraHikDir = Path.Combine(thirdPartyDir, "CameraHIK");
        var mvsRuntimeX64 = ResolveMvsRuntimeX64();
        var cudaBin = ResolveCudaBin();
        var cudnnBin = ResolveCudnnBin(cudaBin);
        var tensorRtRoot = ResolveTensorRtRoot();
        var tensorRtBin = ResolveTensorRtSubDirectory(tensorRtRoot, "bin");
        var tensorRtLib = ResolveTensorRtSubDirectory(tensorRtRoot, "lib");

        return new DesktopNativeRuntimeLayout(
            ortNativeDir,
            thirdPartyDir,
            cameraHikDir,
            mvsRuntimeX64,
            cudaBin,
            cudnnBin,
            tensorRtRoot,
            tensorRtBin,
            tensorRtLib);
    }

    public static string ResolveCudaBin()
    {
        var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
        if (!string.IsNullOrWhiteSpace(cudaPath))
        {
            var bin = Path.Combine(cudaPath, "bin");
            if (Directory.Exists(bin))
            {
                return bin;
            }
        }

        var root = @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA";
        if (!Directory.Exists(root))
        {
            return string.Empty;
        }

        var best = Directory.GetDirectories(root, "v*")
            .Select(path => (path, version: TryParseCudaVersion(Path.GetFileName(path))))
            .Where(item => item.version != null)
            .OrderByDescending(item => item.version)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(best.path))
        {
            return string.Empty;
        }

        var candidate = Path.Combine(best.path, "bin");
        return Directory.Exists(candidate) ? candidate : string.Empty;
    }

    public static string ResolveCudnnBin(string cudaBin)
    {
        var root = @"C:\Program Files\NVIDIA\CUDNN";
        if (!Directory.Exists(root))
        {
            return string.Empty;
        }

        var cudaMajor = TryParseCudaVersion(Path.GetFileName(Path.GetDirectoryName(cudaBin) ?? string.Empty))?.Major;
        var best = Directory.GetDirectories(root, "v*")
            .SelectMany(versionDir =>
            {
                var binRoot = Path.Combine(versionDir, "bin");
                if (!Directory.Exists(binRoot))
                {
                    return Array.Empty<(string path, Version? version)>();
                }

                return Directory.GetDirectories(binRoot)
                    .Select(path => (path, version: TryParseCudaVersion(Path.GetFileName(path))));
            })
            .Where(item => item.version != null)
            .Where(item => cudaMajor == null || item.version!.Major == cudaMajor)
            .OrderByDescending(item => item.version)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(best.path) ? string.Empty : best.path;
    }

    public static string ResolveMvsRuntimeX64()
    {
        try
        {
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (string.IsNullOrWhiteSpace(programFilesX86))
            {
                return string.Empty;
            }

            var candidate = Path.Combine(programFilesX86, "Common Files", "MVS", "Runtime", "Win64_x64");
            return Directory.Exists(candidate) ? candidate : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string ResolveTensorRtRoot()
    {
        foreach (var variable in new[] { "TENSORRT_PATH", "TENSORRT_ROOT", "TensorRT_ROOT" })
        {
            var resolved = NormalizeTensorRtRoot(Environment.GetEnvironmentVariable(variable));
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        var candidates = new[]
        {
            @"C:\Program Files\NVIDIA\TensorRT",
            @"C:\Program Files\TensorRT",
            @"C:\TensorRT"
        };

        foreach (var root in candidates)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var versioned = Directory.GetDirectories(root)
                .Select(NormalizeTensorRtRoot)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(versioned))
            {
                return versioned;
            }
        }

        foreach (var root in new[] { @"C:\" })
        {
            try
            {
                var match = Directory.GetDirectories(root, "TensorRT*")
                    .Select(NormalizeTensorRtRoot)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(match))
                {
                    return match;
                }
            }
            catch
            {
            }
        }

        return string.Empty;
    }

    private static string NormalizeTensorRtRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (!Directory.Exists(path))
        {
            return string.Empty;
        }

        var normalized = Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var leafName = Path.GetFileName(normalized);
        if (string.Equals(leafName, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(leafName, "lib", StringComparison.OrdinalIgnoreCase))
        {
            normalized = Directory.GetParent(normalized)?.FullName ?? normalized;
        }

        var bin = Path.Combine(normalized, "bin");
        var lib = Path.Combine(normalized, "lib");
        return Directory.Exists(bin) || Directory.Exists(lib)
            ? normalized
            : string.Empty;
    }

    private static string ResolveTensorRtSubDirectory(string tensorRtRoot, string childDirectoryName)
    {
        if (string.IsNullOrWhiteSpace(tensorRtRoot))
        {
            return string.Empty;
        }

        var candidate = Path.Combine(tensorRtRoot, childDirectoryName);
        return Directory.Exists(candidate) ? candidate : string.Empty;
    }

    private static Version? TryParseCudaVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? text[1..] : text;
        return Version.TryParse(value, out var version) ? version : null;
    }
}
