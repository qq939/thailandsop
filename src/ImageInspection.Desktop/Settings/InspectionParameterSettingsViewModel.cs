using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoInferenceDemo.ImageInspection.Runtime;

namespace VideoInferenceDemo.ImageInspection.Settings;

public sealed partial class InspectionParameterSettingsViewModel : ObservableObject
{
    private readonly string _path;

    public InspectionParameterSettingsViewModel(string path, InspectionParameterSettings settings)
    {
        _path = path;
        DatabaseProvider = settings.DatabaseProvider;
        ConnectionString = settings.ConnectionString;
        SaveSnapshots = settings.SaveSnapshots;
        SnapshotDirectory = settings.SnapshotDirectory;
        ProtocolType = settings.ProtocolType;
        ProtocolEndpoint = settings.ProtocolEndpoint;
        PublishResults = settings.PublishResults;
        RetentionDaysText = settings.RetentionDays.ToString();
        EnableAutoCleanup = settings.EnableAutoCleanup;
        var plcTrigger = (settings.PlcTrigger ?? new InspectionPlcTriggerOptions()).Normalize();
        PlcTriggerEnabled = plcTrigger.Enabled;
        PlcTriggerHost = plcTrigger.Host;
        PlcTriggerPortText = plcTrigger.Port.ToString();
        PlcTriggerPollIntervalMsText = plcTrigger.PollIntervalMs.ToString();
        PlcTriggerTaskName = plcTrigger.TaskId;
        PlcTriggerRegisterAddressText = plcTrigger.TriggerRegisterAddress.ToString();
        PlcDoneRegisterAddressText = plcTrigger.DoneRegisterAddress.ToString();
        PlcResultRegisterAddressText = plcTrigger.ResultRegisterAddress.ToString();
        PlcReadTimeoutMsText = plcTrigger.ReadTimeoutMs.ToString();
        PlcWriteTimeoutMsText = plcTrigger.WriteTimeoutMs.ToString();
    }

    [ObservableProperty] private string databaseProvider = "SQLite";
    [ObservableProperty] private string connectionString = string.Empty;
    [ObservableProperty] private bool saveSnapshots = true;
    [ObservableProperty] private string snapshotDirectory = "InspectionSnapshots";
    [ObservableProperty] private string protocolType = "TCP";
    [ObservableProperty] private string protocolEndpoint = "127.0.0.1:9000";
    [ObservableProperty] private bool publishResults;
    [ObservableProperty] private string retentionDaysText = "90";
    [ObservableProperty] private bool enableAutoCleanup = true;
    [ObservableProperty] private bool plcTriggerEnabled;
    [ObservableProperty] private string plcTriggerHost = "127.0.0.1";
    [ObservableProperty] private string plcTriggerPortText = "502";
    [ObservableProperty] private string plcTriggerPollIntervalMsText = "100";
    [ObservableProperty] private string plcTriggerTaskName = string.Empty;
    [ObservableProperty] private string plcTriggerRegisterAddressText = "0";
    [ObservableProperty] private string plcDoneRegisterAddressText = "1";
    [ObservableProperty] private string plcResultRegisterAddressText = "2";
    [ObservableProperty] private string plcReadTimeoutMsText = "1000";
    [ObservableProperty] private string plcWriteTimeoutMsText = "1000";
    [ObservableProperty] private bool saveSucceeded;

    [RelayCommand]
    private void Save()
    {
        SaveSucceeded = TrySave();
    }

    public bool TrySave()
    {
        if (!int.TryParse(RetentionDaysText, out var retentionDays))
        {
            retentionDays = 90;
        }

        InspectionParameterSettingsStorage.Save(
            _path,
            new InspectionParameterSettings
            {
                DatabaseProvider = DatabaseProvider,
                ConnectionString = ConnectionString,
                SaveSnapshots = SaveSnapshots,
                SnapshotDirectory = SnapshotDirectory,
                ProtocolType = ProtocolType,
                ProtocolEndpoint = ProtocolEndpoint,
                PublishResults = PublishResults,
                RetentionDays = retentionDays,
                EnableAutoCleanup = EnableAutoCleanup,
                PlcTrigger = BuildPlcTriggerOptions()
            });
        return true;
    }

    private InspectionPlcTriggerOptions BuildPlcTriggerOptions()
    {
        return new InspectionPlcTriggerOptions
        {
            Enabled = PlcTriggerEnabled,
            Host = string.IsNullOrWhiteSpace(PlcTriggerHost) ? "127.0.0.1" : PlcTriggerHost.Trim(),
            Port = ReadInt(PlcTriggerPortText, 502, 1, 65535),
            SlaveAddress = 1,
            PollIntervalMs = ReadInt(PlcTriggerPollIntervalMsText, 100, 50, 2000),
            StationId = string.Empty,
            TaskId = PlcTriggerTaskName?.Trim() ?? string.Empty,
            TriggerRegisterAddress = (ushort)ReadInt(PlcTriggerRegisterAddressText, 0, 0, ushort.MaxValue),
            DoneRegisterAddress = (ushort)ReadInt(PlcDoneRegisterAddressText, 1, 0, ushort.MaxValue),
            ResultRegisterAddress = (ushort)ReadInt(PlcResultRegisterAddressText, 2, 0, ushort.MaxValue),
            ReadTimeoutMs = ReadInt(PlcReadTimeoutMsText, 1000, 100, 10000),
            WriteTimeoutMs = ReadInt(PlcWriteTimeoutMsText, 1000, 100, 10000)
        }.Normalize();
    }

    private static int ReadInt(string? value, int fallback, int min, int max)
    {
        if (!int.TryParse(value, out var parsed))
        {
            parsed = fallback;
        }

        return Math.Clamp(parsed, min, max);
    }
}
