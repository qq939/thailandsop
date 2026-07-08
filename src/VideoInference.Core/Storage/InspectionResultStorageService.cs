using System.Threading.Channels;

namespace VideoInferenceDemo;

public enum InspectionResultStorageMode
{
    SQLiteDaily = 0,
    MySqlPreferredWithSqliteFallback = 1
}

public sealed record InspectionResultStorageOptions(
    InspectionResultStorageMode Mode,
    string MySqlConnectionString,
    int QueueCapacity = 1000,
    TimeSpan? RetryInterval = null);

public sealed record InspectionResultStorageStatus(
    bool Success,
    bool UsedFallback,
    string Message,
    DateTimeOffset Timestamp);

public sealed class InspectionResultStorageService : IDisposable
{
    private readonly Func<InspectionResultStorageOptions> _optionsProvider;
    private readonly SqliteInspectionResultRepository _sqliteRepository = new();
    private readonly Channel<InspectionResultStorageItem> _queue;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writerTask;
    private readonly Task _syncTask;
    private MySqlInspectionResultRepository? _mySqlRepository;
    private string _mySqlConnectionString = string.Empty;

    public InspectionResultStorageService(Func<InspectionResultStorageOptions> optionsProvider)
    {
        _optionsProvider = optionsProvider ?? throw new ArgumentNullException(nameof(optionsProvider));
        var options = Normalize(_optionsProvider());
        _queue = Channel.CreateBounded<InspectionResultStorageItem>(new BoundedChannelOptions(Math.Max(1, options.QueueCapacity))
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _writerTask = Task.Run(() => RunWriterLoopAsync(_cts.Token));
        _syncTask = Task.Run(() => RunSyncLoopAsync(_cts.Token));
    }

    public event Action<InspectionResultStorageStatus>? StatusChanged;

    public bool TryEnqueue(InspectionResultStorageItem item)
    {
        if (_queue.Writer.TryWrite(item))
        {
            return true;
        }

        StatusChanged?.Invoke(new InspectionResultStorageStatus(
            false,
            false,
            "Inspection result storage queue is full; dropped one result.",
            DateTimeOffset.Now));
        return false;
    }

    private async Task RunWriterLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await WriteOneAsync(item, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task WriteOneAsync(InspectionResultStorageItem item, CancellationToken cancellationToken)
    {
        var options = Normalize(_optionsProvider());
        if (options.Mode == InspectionResultStorageMode.SQLiteDaily)
        {
            await _sqliteRepository.WriteAsync(item, InspectionResultMySqlSyncStatus.None, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        try
        {
            await GetMySqlRepository(options.MySqlConnectionString).WriteAsync(item, cancellationToken).ConfigureAwait(false);
            StatusChanged?.Invoke(new InspectionResultStorageStatus(
                true,
                false,
                "Inspection result written to MySQL.",
                DateTimeOffset.Now));
        }
        catch (Exception ex)
        {
            var message = TrimMessage(ex.Message);
            await _sqliteRepository.WriteAsync(
                    item,
                    InspectionResultMySqlSyncStatus.Pending,
                    message,
                    cancellationToken)
                .ConfigureAwait(false);
            StatusChanged?.Invoke(new InspectionResultStorageStatus(
                false,
                true,
                $"MySQL result write failed; saved to SQLite fallback. {message}",
                DateTimeOffset.Now));
        }
    }

    private async Task RunSyncLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var options = Normalize(_optionsProvider());
                var interval = options.RetryInterval ?? TimeSpan.FromSeconds(30);
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                if (options.Mode != InspectionResultStorageMode.MySqlPreferredWithSqliteFallback)
                {
                    continue;
                }

                await SyncPendingAsync(options, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SyncPendingAsync(InspectionResultStorageOptions options, CancellationToken cancellationToken)
    {
        IReadOnlyList<InspectionResultRowSet> pending;
        try
        {
            pending = await _sqliteRepository.ReadPendingAsync(100, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(new InspectionResultStorageStatus(
                false,
                false,
                $"Failed to read SQLite fallback results. {TrimMessage(ex.Message)}",
                DateTimeOffset.Now));
            return;
        }

        if (pending.Count == 0)
        {
            return;
        }

        var mySql = GetMySqlRepository(options.MySqlConnectionString);
        foreach (var rowSet in pending)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await mySql.WriteAsync(rowSet, cancellationToken).ConfigureAwait(false);
                await _sqliteRepository.MarkSyncedAsync(rowSet.Cycle.CycleUuid, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await _sqliteRepository.MarkFailedAsync(rowSet.Cycle.CycleUuid, TrimMessage(ex.Message), cancellationToken)
                    .ConfigureAwait(false);
                StatusChanged?.Invoke(new InspectionResultStorageStatus(
                    false,
                    true,
                    $"MySQL fallback sync failed. {TrimMessage(ex.Message)}",
                    DateTimeOffset.Now));
                break;
            }
        }
    }

    private MySqlInspectionResultRepository GetMySqlRepository(string connectionString)
    {
        if (_mySqlRepository != null &&
            string.Equals(_mySqlConnectionString, connectionString, StringComparison.Ordinal))
        {
            return _mySqlRepository;
        }

        _mySqlConnectionString = connectionString;
        _mySqlRepository = new MySqlInspectionResultRepository(connectionString);
        return _mySqlRepository;
    }

    private static InspectionResultStorageOptions Normalize(InspectionResultStorageOptions options)
    {
        return options with
        {
            MySqlConnectionString = string.IsNullOrWhiteSpace(options.MySqlConnectionString)
                ? "Server=127.0.0.1;Port=3306;Database=image_inspection;Uid=root;Pwd=;Connection Timeout=3;"
                : options.MySqlConnectionString.Trim(),
            QueueCapacity = Math.Clamp(options.QueueCapacity, 1, 10000)
        };
    }

    private static string TrimMessage(string message)
    {
        return string.IsNullOrWhiteSpace(message)
            ? "Unknown error."
            : message.Length <= 500
                ? message
                : message[..500];
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        try
        {
            _writerTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        _cts.Cancel();
        try
        {
            _syncTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        _cts.Dispose();
    }
}
