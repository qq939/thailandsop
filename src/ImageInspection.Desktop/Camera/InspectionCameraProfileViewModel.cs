using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VideoInferenceDemo.ImageInspection.Camera;

public sealed partial class InspectionCameraProfileViewModel : ObservableObject
{
    private readonly CameraProviderRegistry _cameraProviders;

    public InspectionCameraProfileViewModel(InspectionCameraProfile profile, CameraProviderRegistry cameraProviders)
    {
        _cameraProviders = cameraProviders;
        Id = profile.Id;
        Name = profile.Name;
        Enabled = profile.Enabled;
        SelectedProviderId = profile.ProviderId;
        CameraIndex = profile.CameraIndex.ToString(CultureInfo.InvariantCulture);
        DeviceId = profile.DeviceId;
        OpenCvSource = profile.OpenCvSource;
        OpenCvBackend = profile.OpenCvBackend;
        SelectedTriggerMode = profile.TriggerMode;
        TargetFps = profile.TargetFps.ToString(CultureInfo.InvariantCulture);
        SaveImages = profile.SaveImages;
        SaveRoiImages = profile.SaveRoiImages;
        ImageSaveDirectory = profile.ImageSaveDirectory;
        ImageFileNamePattern = string.IsNullOrWhiteSpace(profile.ImageFileNamePattern)
            ? "{Timestamp:yyyyMMdd_HHmmssfff}.jpg"
            : profile.ImageFileNamePattern;
        DeviceDiscoveryStatus = BuildPendingRefreshStatus(SelectedProviderId);
        SelectedDeviceSummary = "No device details available.";
    }

    public ObservableCollection<CameraDeviceInfo> AvailableDevices { get; } = [];

    public IReadOnlyList<InspectionCameraTriggerModeOption> TriggerModes { get; } =
    [
        new(CameraTriggerMode.Software, "软触发"),
        new(CameraTriggerMode.HardwareLine0, "硬触发（自动输入线）"),
        new(CameraTriggerMode.Continuous, "连续采集")
    ];

    [ObservableProperty] private string id = string.Empty;
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private bool enabled = true;
    [ObservableProperty] private string selectedProviderId = CameraProviderIds.OpenCv;
    [ObservableProperty] private CameraDeviceInfo? selectedDevice;
    [ObservableProperty] private string cameraIndex = "0";
    [ObservableProperty] private string deviceId = string.Empty;
    [ObservableProperty] private string openCvSource = string.Empty;
    [ObservableProperty] private string openCvBackend = string.Empty;
    [ObservableProperty] private CameraTriggerMode selectedTriggerMode = CameraTriggerMode.Software;
    [ObservableProperty] private string targetFps = "5";
    [ObservableProperty] private bool saveImages = true;
    [ObservableProperty] private bool saveRoiImages;
    [ObservableProperty] private string imageSaveDirectory = "InspectionImages";
    [ObservableProperty] private string imageFileNamePattern = "{Timestamp:yyyyMMdd_HHmmssfff}.jpg";
    [ObservableProperty] private string deviceDiscoveryStatus = string.Empty;
    [ObservableProperty] private string selectedDeviceSummary = string.Empty;
    [ObservableProperty] private string errorMessage = string.Empty;

    public bool UsesDeviceIdentifiers => !string.Equals(SelectedProviderId, CameraProviderIds.OpenCv, StringComparison.OrdinalIgnoreCase);

    public bool IsOpenCvProvider => !UsesDeviceIdentifiers;

    public bool HasDiscoveredDevices => AvailableDevices.Count > 0;

    public string ProviderDisplayName => _cameraProviders.GetDisplayName(SelectedProviderId);

    public string SelectorText => UsesDeviceIdentifiers
        ? (!string.IsNullOrWhiteSpace(DeviceId) ? DeviceId.Trim() : "Manual device id")
        : $"Index {CameraIndex.Trim()}";

    public string SidebarTitle => string.IsNullOrWhiteSpace(Name) ? "Camera" : Name;

    public string SidebarSubtitle => $"{ProviderDisplayName} / {SelectorText} / {FormatTriggerMode(SelectedTriggerMode)}";

    public string SidebarStateText => Enabled ? "Enabled" : "Disabled";

    public string SaveModeText => SaveImages
        ? SaveRoiImages ? "Save images + ROI" : "Save images"
        : "Preview only";

    partial void OnNameChanged(string value) => RaiseSidebarChanged();

    partial void OnEnabledChanged(bool value)
    {
        RaiseSidebarChanged();
        OnPropertyChanged(nameof(SidebarStateText));
    }

    partial void OnSaveImagesChanged(bool value)
    {
        RaiseSidebarChanged();
        OnPropertyChanged(nameof(SaveModeText));
    }

    partial void OnSaveRoiImagesChanged(bool value)
    {
        RaiseSidebarChanged();
        OnPropertyChanged(nameof(SaveModeText));
    }

    partial void OnSelectedProviderIdChanged(string value)
    {
        OnPropertyChanged(nameof(UsesDeviceIdentifiers));
        OnPropertyChanged(nameof(IsOpenCvProvider));
        OnPropertyChanged(nameof(ProviderDisplayName));
        ResetDeviceDiscovery(BuildPendingRefreshStatus(value));
        RaiseSidebarChanged();
    }

    partial void OnSelectedDeviceChanged(CameraDeviceInfo? value)
    {
        if (value != null)
        {
            DeviceId = value.DeviceId;
        }

        UpdateSelectedDeviceSummary();
    }

    partial void OnDeviceIdChanged(string value)
    {
        SyncSelectedDeviceToDeviceId();
        UpdateSelectedDeviceSummary();
        RaiseSidebarChanged();
    }

    partial void OnCameraIndexChanged(string value) => RaiseSidebarChanged();

    partial void OnTargetFpsChanged(string value) => RaiseSidebarChanged();

    partial void OnSelectedTriggerModeChanged(CameraTriggerMode value) => RaiseSidebarChanged();

    [RelayCommand]
    private async Task RefreshDevices()
    {
        ErrorMessage = string.Empty;
        AvailableDevices.Clear();
        SelectedDevice = null;

        try
        {
            DeviceDiscoveryStatus = $"Refreshing {_cameraProviders.GetDisplayName(SelectedProviderId)} devices...";
            var devices = await Task.Run(() => _cameraProviders.EnumerateDevices(SelectedProviderId));
            foreach (var device in devices)
            {
                AvailableDevices.Add(device);
            }

            DeviceDiscoveryStatus = BuildDeviceDiscoveryStatus(devices.Count);
            SyncSelectedDeviceToDeviceId();
            CameraDiagnostics.Info("inspection-camera-config", $"Enumerated {devices.Count} camera(s) for provider '{SelectedProviderId}'.");
        }
        catch (Exception ex)
        {
            DeviceDiscoveryStatus = $"{_cameraProviders.GetDisplayName(SelectedProviderId)} discovery failed.";
            ErrorMessage = $"Device enumeration failed: {ex.Message}";
            CameraDiagnostics.Error("inspection-camera-config", $"Failed to enumerate cameras for provider '{SelectedProviderId}'.", ex);
        }

        OnPropertyChanged(nameof(HasDiscoveredDevices));
        UpdateSelectedDeviceSummary();
    }

    public InspectionCameraProfile Build()
    {
        var profile = new InspectionCameraProfile
        {
            Id = Id,
            Name = Name,
            Enabled = Enabled,
            ProviderId = SelectedProviderId,
            DeviceId = UsesDeviceIdentifiers ? DeviceId : string.Empty,
            OpenCvSource = OpenCvSource,
            OpenCvBackend = OpenCvBackend,
            TriggerMode = SelectedTriggerMode,
            SaveImages = SaveImages,
            SaveRoiImages = SaveRoiImages,
            ImageSaveDirectory = ImageSaveDirectory,
            ImageFileNamePattern = ImageFileNamePattern
        };

        if (int.TryParse(CameraIndex, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cameraIndex))
        {
            profile.CameraIndex = cameraIndex;
        }

        if (double.TryParse(TargetFps, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps))
        {
            profile.TargetFps = fps;
        }

        return profile.Normalize(1);
    }

    private void ResetDeviceDiscovery(string status)
    {
        ErrorMessage = string.Empty;
        AvailableDevices.Clear();
        SelectedDevice = null;
        DeviceDiscoveryStatus = status;
        OnPropertyChanged(nameof(HasDiscoveredDevices));
        UpdateSelectedDeviceSummary();
    }

    private string BuildPendingRefreshStatus(string providerId)
    {
        return string.Empty;
    }

    private string BuildDeviceDiscoveryStatus(int count)
    {
        if (!UsesDeviceIdentifiers)
        {
            return count > 0
                ? $"Discovered {count} camera(s). OpenCV still selects by camera index."
                : "OpenCV selects devices by camera index. Try 0 / 1 / 2 when multiple cameras are connected.";
        }

        return count > 0
            ? $"Discovered {count} {_cameraProviders.GetDisplayName(SelectedProviderId)} camera(s)."
            : "No devices discovered yet. You can still enter a device id manually.";
    }

    private static string FormatTriggerMode(CameraTriggerMode triggerMode)
    {
        return triggerMode switch
        {
            CameraTriggerMode.Software => "软触发",
            CameraTriggerMode.HardwareLine0 => "硬触发（自动输入线）",
            CameraTriggerMode.Continuous => "连续采集",
            _ => triggerMode.ToString()
        };
    }

    private void SyncSelectedDeviceToDeviceId()
    {
        if (!UsesDeviceIdentifiers)
        {
            SelectedDevice = null;
            return;
        }

        var deviceId = DeviceId.Trim();
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            SelectedDevice = null;
            return;
        }

        SelectedDevice = AvailableDevices.FirstOrDefault(device =>
            string.Equals(device.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(device.SerialNumber, deviceId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(device.UserDefinedName, deviceId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(device.DisplayName, deviceId, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateSelectedDeviceSummary()
    {
        if (SelectedDevice == null)
        {
            SelectedDeviceSummary = HasDiscoveredDevices
                ? "No discovered device selected."
                : "No device details available.";
            return;
        }

        SelectedDeviceSummary =
            $"Display: {SelectedDevice.DisplayName} | Model: {SelectedDevice.ModelName ?? "-"} | Serial: {SelectedDevice.SerialNumber ?? "-"} | Alias: {SelectedDevice.UserDefinedName ?? "-"}";
    }

    private void RaiseSidebarChanged()
    {
        OnPropertyChanged(nameof(SidebarTitle));
        OnPropertyChanged(nameof(SidebarSubtitle));
        OnPropertyChanged(nameof(SelectorText));
    }
}

public sealed record InspectionCameraTriggerModeOption(CameraTriggerMode Mode, string DisplayName);
