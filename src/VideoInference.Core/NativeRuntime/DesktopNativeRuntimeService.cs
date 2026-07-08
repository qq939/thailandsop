using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace VideoInferenceDemo;

public sealed class DesktopNativeRuntimeService : IDesktopNativeRuntimeService
{
    public void Initialize()
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "native_runtime_init.log");
        var lines = new List<string>();
        try
        {
            Append(lines, $"Initialize started: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}");
            Append(lines, $"BaseDirectory: {AppContext.BaseDirectory}");

            var layout = DesktopNativeRuntimePathResolver.Resolve(AppContext.BaseDirectory);
            Append(lines, $"ORT native: {layout.OrtNativeDir}");
            Append(lines, $"ThirdParty: {layout.ThirdPartyDir}");
            Append(lines, $"CameraHIK: {layout.CameraHikDir}");
            Append(lines, $"MVS Win64: {layout.MvsRuntimeX64}");
            Append(lines, $"CUDA bin: {layout.CudaBin}");
            Append(lines, $"cuDNN bin: {layout.CudnnBin}");
            Append(lines, $"TensorRT root: {layout.TensorRtRoot}");
            Append(lines, $"TensorRT bin: {layout.TensorRtBin}");
            Append(lines, $"TensorRT lib: {layout.TensorRtLib}");

            var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .ToList();
            Append(lines, $"Original PATH entries: {paths.Count}");

            paths.RemoveAll(p => p.Contains(@"\NVIDIA\CUDNN\v9.9\", StringComparison.OrdinalIgnoreCase));
            paths.RemoveAll(p => p.Contains(@"\miniconda", StringComparison.OrdinalIgnoreCase));
            paths.RemoveAll(p => p.Contains(@"\anaconda", StringComparison.OrdinalIgnoreCase));
            paths.RemoveAll(p => p.Contains(@"\Common Files\MVS\Runtime\Win32_", StringComparison.OrdinalIgnoreCase));

            PrependIfDirectory(paths, layout.OrtNativeDir);
            PrependIfDirectory(paths, layout.MvsRuntimeX64);
            PrependIfDirectory(paths, layout.ThirdPartyDir);
            PrependIfDirectory(paths, layout.CameraHikDir);
            PrependIfDirectory(paths, layout.CudaBin);
            PrependIfDirectory(paths, layout.CudnnBin);
            PrependIfDirectory(paths, layout.TensorRtBin);
            PrependIfDirectory(paths, layout.TensorRtLib);

            var joinedPath = string.Join(Path.PathSeparator, paths);
            Append(lines, $"Updated PATH entries: {paths.Count}");
            Append(lines, $"Updated PATH length: {joinedPath.Length}");

            ConfigureDllSearch(layout);
            Append(lines, "ConfigureDllSearch: completed");

            Environment.SetEnvironmentVariable("PATH", joinedPath);
            Append(lines, "PATH update: completed");

            Append(lines, "Skip eager native preload probe during startup");
            Append(lines, "Skip eager CUDA provider probe during startup");
            Append(lines, "Initialize finished successfully");
        }
        catch (Exception ex)
        {
            Append(lines, "Initialize failed");
            Append(lines, ex.ToString());
        }
        finally
        {
            TryWriteLog(logPath, lines);
        }
    }

    private static void Append(List<string> lines, string message)
    {
        lines.Add(message);
    }

    private static void TryWriteLog(string path, IEnumerable<string> lines)
    {
        try
        {
            File.WriteAllText(path, string.Join(Environment.NewLine, lines), Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static void PrependIfDirectory(List<string> paths, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) &&
            Directory.Exists(path) &&
            !paths.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            paths.Insert(0, path);
        }
    }

    private static void ConfigureDllSearch(DesktopNativeRuntimeLayout layout)
    {
        try
        {
            const uint loadLibrarySearchDefaultDirs = 0x00001000;
            const uint loadLibrarySearchUserDirs = 0x00000400;
            _ = SetDefaultDllDirectories(loadLibrarySearchDefaultDirs | loadLibrarySearchUserDirs);
            AddDirectory(layout.OrtNativeDir);
            AddDirectory(layout.ThirdPartyDir);
            AddDirectory(layout.CameraHikDir);
            AddDirectory(layout.MvsRuntimeX64);
            AddDirectory(layout.CudaBin);
            AddDirectory(layout.CudnnBin);
            AddDirectory(layout.TensorRtBin);
            AddDirectory(layout.TensorRtLib);
        }
        catch
        {
        }
    }

    private static void AddDirectory(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            _ = AddDllDirectory(path);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr AddDllDirectory(string newDirectory);
}
