namespace VideoInferenceDemo;

public sealed class FingerprintRecognitionHost : IDisposable
{
    private readonly PersonnelRepository _personnelRepository;
    private readonly List<FingerprintRecognitionMonitor> _monitors = new();
    private readonly Dictionary<string, FingerprintModuleOptions> _moduleOptions = new(StringComparer.OrdinalIgnoreCase);

    public FingerprintRecognitionHost(PersonnelRepository personnelRepository)
    {
        _personnelRepository = personnelRepository ?? throw new ArgumentNullException(nameof(personnelRepository));
    }

    public event Action<FingerprintPersonnelRecognition>? Recognized;
    public event Action<string>? Error;

    public int RunningModuleCount => _monitors.Count;

    /// <summary>暂停指定指纹模块的监控，释放串口。</summary>
    public void Suspend(string moduleId)
    {
        var monitor = _monitors.FirstOrDefault(m =>
            string.Equals(m.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase));
        if (monitor == null)
        {
            return;
        }

        CameraDiagnostics.Info("fingerprint", $"Suspending fingerprint monitor for module '{moduleId}'.");
        monitor.StopAsync().GetAwaiter().GetResult();
        _monitors.Remove(monitor);
        monitor.Dispose();
    }

    /// <summary>恢复指定指纹模块的监控，重新打开串口。</summary>
    public bool Resume(string moduleId)
    {
        if (!_moduleOptions.TryGetValue(moduleId, out var options))
        {
            CameraDiagnostics.Error("fingerprint", $"Cannot resume module '{moduleId}': options not found.");
            return false;
        }

        try
        {
            var client = NModbusRegisterClient.Create(options);
            var module = new FingerprintModule(client, options.SlaveAddress);
            var monitor = new FingerprintRecognitionMonitor(
                options.Id,
                options.Name,
                module,
                _personnelRepository,
                TimeSpan.FromMilliseconds(options.PollIntervalMs),
                TimeSpan.FromMilliseconds(options.DuplicateSuppressMs));
            monitor.Recognized += OnMonitorRecognized;
            monitor.Error += OnMonitorError;
            monitor.Start();
            _monitors.Add(monitor);
            CameraDiagnostics.Info("fingerprint", $"Resumed fingerprint monitor for module '{moduleId}'.");
            return true;
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Error("fingerprint", $"Failed to resume fingerprint module '{moduleId}'.", ex);
            Error?.Invoke($"Failed to resume fingerprint module '{options?.Name}': {ex.Message}.");
            return false;
        }
    }

    public void Start(IEnumerable<FingerprintModuleOptions>? moduleOptions)
    {
        Stop();
        _moduleOptions.Clear();

        foreach (var options in (moduleOptions ?? Array.Empty<FingerprintModuleOptions>())
                     .Select(item => item.Normalize())
                     .Where(item => item.Enabled))
        {
            try
            {
                var client = NModbusRegisterClient.Create(options);
                var module = new FingerprintModule(client, options.SlaveAddress);
                var monitor = new FingerprintRecognitionMonitor(
                    options.Id,
                    options.Name,
                    module,
                    _personnelRepository,
                    TimeSpan.FromMilliseconds(options.PollIntervalMs),
                    TimeSpan.FromMilliseconds(options.DuplicateSuppressMs));
                monitor.Recognized += OnMonitorRecognized;
                monitor.Error += OnMonitorError;
                monitor.Start();
                _monitors.Add(monitor);
                _moduleOptions[options.Id] = options;
                CameraDiagnostics.Info(
                    "fingerprint",
                    $"Started fingerprint module '{options.Name}'. Id={options.Id}, Connection={options.ConnectionKind}, SlaveAddress={options.SlaveAddress}.");
            }
            catch (Exception ex)
            {
                CameraDiagnostics.Error("fingerprint", $"Failed to start fingerprint module '{options.Name}'.", ex);
                Error?.Invoke($"Failed to start fingerprint module '{options.Name}': {ex.Message}. See camera_debug.log for details.");
            }
        }
    }

    public void Stop()
    {
        foreach (var monitor in _monitors)
        {
            monitor.Recognized -= OnMonitorRecognized;
            monitor.Error -= OnMonitorError;
            monitor.Dispose();
        }

        _monitors.Clear();
    }

    public void Dispose()
    {
        Stop();
    }

    private void OnMonitorRecognized(FingerprintPersonnelRecognition recognition)
    {
        Recognized?.Invoke(recognition);
    }

    private void OnMonitorError(string message)
    {
        Error?.Invoke(message);
    }
}
