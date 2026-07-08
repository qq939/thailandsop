using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace VideoInferenceDemo;

public static class OrtNativeLibraryLoader
{
    private static readonly object SyncRoot = new();
    private static readonly HashSet<string> AttemptedCandidates = new(StringComparer.Ordinal);

    public static void TryPreload(string? configuredPath)
    {
        foreach (var candidate in BuildCandidates(configuredPath))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            lock (SyncRoot)
            {
                if (!AttemptedCandidates.Add(candidate))
                {
                    continue;
                }
            }

            try
            {
                if (NativeLibrary.TryLoad(candidate, out _))
                {
                    return;
                }
            }
            catch
            {
            }
        }
    }

    private static IEnumerable<string> BuildCandidates(string? configuredPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            candidates.AddRange(NormalizeMainLibraryCandidates(configuredPath));
        }

        foreach (var directory in EnumerateSearchDirectories(configuredPath))
        {
            foreach (var fileName in GetMainLibraryNames())
            {
                candidates.Add(Path.Combine(directory, fileName));
            }

            foreach (var discovered in DiscoverMainLibraries(directory))
            {
                candidates.Add(discovered);
            }
        }

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal);
    }

    private static IEnumerable<string> EnumerateSearchDirectories(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (Directory.Exists(configuredPath))
            {
                yield return configuredPath;
            }
            else
            {
                var maybeDirectory = Path.GetDirectoryName(configuredPath);
                if (!string.IsNullOrWhiteSpace(maybeDirectory))
                {
                    yield return maybeDirectory;
                }
            }
        }

        yield return Path.Combine(AppContext.BaseDirectory, "native");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "native");
        yield return AppContext.BaseDirectory;
    }

    private static IEnumerable<string> NormalizeMainLibraryCandidates(string configuredPath)
    {
        if (Directory.Exists(configuredPath))
        {
            return GetMainLibraryNames()
                .Select(fileName => Path.Combine(configuredPath, fileName))
                .Concat(DiscoverMainLibraries(configuredPath));
        }

        var fileName = Path.GetFileName(configuredPath);
        var libraryNames = GetMainLibraryNames();
        if (libraryNames.Contains(fileName, StringComparer.Ordinal))
        {
            return new[] { configuredPath };
        }

        var maybeDirectory = Path.GetDirectoryName(configuredPath);
        return string.IsNullOrWhiteSpace(maybeDirectory)
            ? new[] { configuredPath }
            : libraryNames.Select(name => Path.Combine(maybeDirectory, name));
    }

    private static IReadOnlyList<string> GetMainLibraryNames()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new[]
            {
                "onnxruntime.dll"
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new[]
            {
                "libonnxruntime.so",
                "libonnxruntime.so.1"
            };
        }

        return new[]
        {
            "libonnxruntime.dylib",
            "libonnxruntime.so",
            "onnxruntime.dll"
        };
    }

    private static IEnumerable<string> DiscoverMainLibraries(string directory)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory.EnumerateFiles(directory, "libonnxruntime.so*")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
