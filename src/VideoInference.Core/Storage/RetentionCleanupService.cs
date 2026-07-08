using System.Globalization;

namespace VideoInferenceDemo;

public sealed class RetentionCleanupOptions
{
    public string ResultsDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "results");
    public List<string> RecordingDirectories { get; set; } = ["Recordings"];
    public List<string> InspectionImageDirectories { get; set; } = ["InspectionImages"];
    public int RetentionDays { get; set; } = 90;
    public bool EnableAutoCleanup { get; set; } = true;
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);
    public int BatchSize { get; set; } = 32;
    public TimeSpan BatchDelay { get; set; } = TimeSpan.FromMilliseconds(50);

    public RetentionCleanupOptions Normalize()
    {
        return new RetentionCleanupOptions
        {
            ResultsDirectory = NormalizeDirectory(ResultsDirectory, Path.Combine(AppContext.BaseDirectory, "results")),
            RecordingDirectories = NormalizeDirectories(RecordingDirectories, "Recordings"),
            InspectionImageDirectories = NormalizeDirectories(InspectionImageDirectories, "InspectionImages"),
            RetentionDays = Math.Clamp(RetentionDays <= 0 ? 90 : RetentionDays, 1, 3650),
            EnableAutoCleanup = EnableAutoCleanup,
            StartupDelay = StartupDelay < TimeSpan.Zero ? TimeSpan.Zero : StartupDelay,
            Interval = Interval <= TimeSpan.Zero ? TimeSpan.FromHours(24) : Interval,
            BatchSize = Math.Max(1, BatchSize),
            BatchDelay = BatchDelay < TimeSpan.Zero ? TimeSpan.Zero : BatchDelay
        };
    }

    private static List<string> NormalizeDirectories(IEnumerable<string>? directories, string fallback)
    {
        var list = (directories ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => NormalizeDirectory(item, fallback))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (list.Count == 0)
        {
            list.Add(NormalizeDirectory(fallback, fallback));
        }

        return list;
    }

    private static string NormalizeDirectory(string value, string fallback)
    {
        var path = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, path);
        }

        return Path.GetFullPath(path);
    }
}

public sealed class RetentionCleanupService : IDisposable
{
    private static readonly string[] ResultDbExtensions = [".db", ".db-wal", ".db-shm"];
    private static readonly string[] LegacyImageExtensions = [".jpg", ".jpeg", ".png"];

    private readonly RetentionCleanupOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private Task? _worker;

    public RetentionCleanupService(RetentionCleanupOptions options)
    {
        _options = (options ?? new RetentionCleanupOptions()).Normalize();
    }

    public void Start()
    {
        if (!_options.EnableAutoCleanup || _worker != null)
            return;

        _worker = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    public Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        return CleanupAsync(cancellationToken);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _worker?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        _cts.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_options.StartupDelay, cancellationToken).ConfigureAwait(false);
            while (!cancellationToken.IsCancellationRequested)
            {
                await CleanupAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(_options.Interval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Now.Date);
        var cutoffExclusive = today.AddDays(-_options.RetentionDays);
        var throttle = new CleanupThrottle(_options.BatchSize, _options.BatchDelay);

        await CleanupResultDatabasesAsync(cutoffExclusive, today, throttle, cancellationToken).ConfigureAwait(false);
        await CleanupDatedDirectoriesAsync(_options.RecordingDirectories, cutoffExclusive, today, throttle, cancellationToken).ConfigureAwait(false);
        await CleanupInspectionImagesAsync(cutoffExclusive, today, throttle, cancellationToken).ConfigureAwait(false);
    }

    private async Task CleanupResultDatabasesAsync(
        DateOnly cutoffExclusive,
        DateOnly today,
        CleanupThrottle throttle,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_options.ResultsDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_options.ResultsDirectory, "*.db", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stem = Path.GetFileNameWithoutExtension(file);
            if (!DateOnly.TryParseExact(stem, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            if (date >= cutoffExclusive || date >= today)
            {
                continue;
            }

            foreach (var suffix in ResultDbExtensions)
            {
                var companion = suffix == ".db"
                    ? file
                    : Path.Combine(Path.GetDirectoryName(file)!, $"{stem}{suffix}");
                TryDeleteFile(companion);
                await throttle.TickAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task CleanupDatedDirectoriesAsync(
        IEnumerable<string> roots,
        DateOnly cutoffExclusive,
        DateOnly today,
        CleanupThrottle throttle,
        CancellationToken cancellationToken)
    {
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(directory);
                if (!DateOnly.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    continue;
                }

                if (date >= cutoffExclusive || date >= today)
                {
                    continue;
                }

                TryDeleteDirectory(directory);
                await throttle.TickAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task CleanupInspectionImagesAsync(
        DateOnly cutoffExclusive,
        DateOnly today,
        CleanupThrottle throttle,
        CancellationToken cancellationToken)
    {
        await CleanupDatedDirectoriesAsync(_options.InspectionImageDirectories, cutoffExclusive, today, throttle, cancellationToken)
            .ConfigureAwait(false);

        var cutoffLastWrite = DateTime.Now.Date.AddDays(-_options.RetentionDays);
        foreach (var root in _options.InspectionImageDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in EnumerateLegacyImageFiles(root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsInsideDateDirectory(root, file))
                {
                    continue;
                }

                if (!IsLegacyImageFile(file))
                {
                    continue;
                }

                if (File.GetLastWriteTime(file) >= cutoffLastWrite)
                {
                    continue;
                }

                TryDeleteFile(file);
                await throttle.TickAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static IEnumerable<string> EnumerateLegacyImageFiles(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Error("retention-cleanup", $"Failed to enumerate legacy image directory '{root}'.", ex);
            return Array.Empty<string>();
        }
    }

    private static bool IsLegacyImageFile(string file)
    {
        var extension = Path.GetExtension(file);
        return LegacyImageExtensions.Any(item => item.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsInsideDateDirectory(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        var firstPart = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).FirstOrDefault();
        return DateOnly.TryParseExact(firstPart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Error("retention-cleanup", $"Failed to delete file '{path}'.", ex);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Error("retention-cleanup", $"Failed to delete directory '{path}'.", ex);
        }
    }

    private sealed class CleanupThrottle
    {
        private readonly int _batchSize;
        private readonly TimeSpan _batchDelay;
        private int _count;

        public CleanupThrottle(int batchSize, TimeSpan batchDelay)
        {
            _batchSize = Math.Max(1, batchSize);
            _batchDelay = batchDelay < TimeSpan.Zero ? TimeSpan.Zero : batchDelay;
        }

        public async Task TickAsync(CancellationToken cancellationToken)
        {
            _count++;
            if (_count % _batchSize == 0 && _batchDelay > TimeSpan.Zero)
            {
                await Task.Delay(_batchDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
