namespace VideoInferenceDemo;

public sealed class FingerprintRecognitionMonitor : IDisposable
{
    private readonly IFingerprintModule _module;
    private readonly PersonnelRepository _personnelRepository;
    private readonly string _moduleId;
    private readonly string _moduleName;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _duplicateSuppress;
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private int? _lastFingerprintId;
    private long _lastFingerprintUtcMs;

    public FingerprintRecognitionMonitor(
        string moduleId,
        string moduleName,
        IFingerprintModule module,
        PersonnelRepository personnelRepository,
        TimeSpan pollInterval,
        TimeSpan duplicateSuppress)
    {
        _moduleId = string.IsNullOrWhiteSpace(moduleId) ? "fingerprint" : moduleId.Trim();
        _moduleName = string.IsNullOrWhiteSpace(moduleName) ? _moduleId : moduleName.Trim();
        _module = module ?? throw new ArgumentNullException(nameof(module));
        _personnelRepository = personnelRepository ?? throw new ArgumentNullException(nameof(personnelRepository));
        _pollInterval = pollInterval <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(200) : pollInterval;
        _duplicateSuppress = duplicateSuppress < TimeSpan.Zero ? TimeSpan.Zero : duplicateSuppress;
    }

    public event Action<FingerprintPersonnelRecognition>? Recognized;
    public event Action<string>? Error;

    public string ModuleId => _moduleId;
    public bool IsRunning => _worker != null;

    public void Start()
    {
        if (_worker != null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => RunAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts == null)
        {
            return;
        }

        try
        {
            cts.Cancel();
            if (_worker != null)
            {
                await _worker.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
            _worker = null;
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _module.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _module.ReadRecognitionResultAsync(ct).ConfigureAwait(false);
                if (result.Success && result.FingerprintId.HasValue && ShouldPublish(result.FingerprintId.Value))
                {
                    var personnel = _personnelRepository.GetByFingerprintId(result.FingerprintId.Value, _moduleId);
                    Recognized?.Invoke(new FingerprintPersonnelRecognition(
                        _moduleId,
                        _moduleName,
                        result,
                        personnel,
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                CameraDiagnostics.Error("fingerprint", "Fingerprint recognition polling failed.", ex);
                Error?.Invoke($"Fingerprint recognition polling failed: {ex.Message}. See camera_debug.log for details.");
            }

            await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
        }
    }

    private bool ShouldPublish(int fingerprintId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (_lastFingerprintId == fingerprintId &&
            _duplicateSuppress > TimeSpan.Zero &&
            now - _lastFingerprintUtcMs < _duplicateSuppress.TotalMilliseconds)
        {
            return false;
        }

        _lastFingerprintId = fingerprintId;
        _lastFingerprintUtcMs = now;
        return true;
    }
}
