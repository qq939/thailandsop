using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VideoInferenceDemo;

public sealed partial class CameraProfileViewModel : ObservableObject
{
    private readonly CameraProviderRegistry _cameraProviders;
    private readonly CameraProfile _hiddenSettings;
    private string _id = string.Empty;

    public CameraProfileViewModel(CameraProfile profile, CameraProviderRegistry cameraProviders)
    {
        _cameraProviders = cameraProviders;
        var normalized = (profile ?? CameraProfile.CreateDefault(1)).Normalize(1);
        _hiddenSettings = normalized.Clone();

        _id = normalized.Id;
        name = normalized.Name;
        isEnabled = normalized.Enabled;
        autoStart = normalized.AutoStart;
        selectedProviderId = normalized.ProviderId;
        cameraIndexText = normalized.CameraIndex.ToString(CultureInfo.InvariantCulture);
        deviceIdText = normalized.DeviceId;
        targetFpsText = normalized.TargetFps.ToString(CultureInfo.InvariantCulture);
        rotation = normalized.Rotation;
        mirrorMode = normalized.MirrorMode;
        useSourcePtsForVideo = normalized.UseSourcePtsForVideo;
        primaryTaskId = normalized.PrimaryTaskId;
        selectedSopProfileId = normalized.SelectedSopProfileId;
        enableSopAnalysis = normalized.EnableSopAnalysis;
        analysisFrameWindowSizeText = normalized.AnalysisFrameWindowSize.ToString(CultureInfo.InvariantCulture);
        analysisStateWindowSizeText = normalized.AnalysisStateWindowSize.ToString(CultureInfo.InvariantCulture);
        analysisHoldFramesText = normalized.AnalysisHoldFrames.ToString(CultureInfo.InvariantCulture);
        sopWindowMsText = normalized.SopWindowMs.ToString(CultureInfo.InvariantCulture);
        sopMinScoreQ1000Text = normalized.SopMinScoreQ1000.ToString(CultureInfo.InvariantCulture);
        sopMinVisibleRatioQ1000Text = normalized.SopMinVisibleRatioQ1000.ToString(CultureInfo.InvariantCulture);

        ocrEnabled = normalized.OcrEnabled;
        ocrRoiXText = normalized.OcrRoiX.ToString(CultureInfo.InvariantCulture);
        ocrRoiYText = normalized.OcrRoiY.ToString(CultureInfo.InvariantCulture);
        ocrRoiWidthText = normalized.OcrRoiWidth.ToString(CultureInfo.InvariantCulture);
        ocrRoiHeightText = normalized.OcrRoiHeight.ToString(CultureInfo.InvariantCulture);

        enableRecording = normalized.EnableCameraRecording;
        recordingRootDirectoryText = normalized.RecordingRootDirectory;
        recordingSegmentMinutesText = normalized.RecordingSegmentMinutes.ToString(CultureInfo.InvariantCulture);
        selectedRecordingContainerFormat = normalized.RecordingContainerFormat;
        selectedRecordingVideoEncoder = normalized.RecordingVideoEncoder;
        recordingBitrateMbpsText = normalized.RecordingBitrateMbps.ToString(CultureInfo.InvariantCulture);
                deviceDiscoveryStatus = BuildPendingRefreshStatus(selectedProviderId);
        selectedDeviceSummary = "No discovered device selected.";
    }

    public ObservableCollection<CameraDeviceInfo> AvailableDevices { get; } = new();
    public IReadOnlyList<string> RecordingContainerFormats { get; } = new[] { "mkv", "mp4" };
    public IReadOnlyList<string> RecordingVideoEncoders { get; } = new[] { "hevc_nvenc", "h264_nvenc" };
    public IReadOnlyList<CameraRotation> RotationOptions { get; } = new[] { CameraRotation.None, CameraRotation.Rotate90, CameraRotation.Rotate180, CameraRotation.Rotate270 };
    public IReadOnlyList<CameraMirrorMode> MirrorOptions { get; } = new[] { CameraMirrorMode.None, CameraMirrorMode.Horizontal, CameraMirrorMode.Vertical, CameraMirrorMode.Both };
    public ObservableCollection<SopProfileItem> AvailableSopProfiles { get; } = new();

    public string Id
    {
        get => _id;
        private set => SetProperty(ref _id, value);
    }

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private bool isEnabled = true;

    [ObservableProperty]
    private bool autoStart;

    [ObservableProperty]
    private string selectedProviderId = CameraProviderIds.OpenCv;

    [ObservableProperty]
    private CameraDeviceInfo? selectedDevice;

    [ObservableProperty]
    private string cameraIndexText = "0";

    [ObservableProperty]
    private string deviceIdText = string.Empty;

    [ObservableProperty]
    private string targetFpsText = "30";

    [ObservableProperty]
    private CameraRotation rotation = CameraRotation.None;

    [ObservableProperty]
    private CameraMirrorMode mirrorMode = CameraMirrorMode.None;

    [ObservableProperty]
    private bool useSourcePtsForVideo = true;

    [ObservableProperty]
    private string primaryTaskId = string.Empty;

    [ObservableProperty]
    private string selectedSopProfileId = string.Empty;

    [ObservableProperty]
    private bool enableSopAnalysis = true;

    [ObservableProperty]
    private string analysisFrameWindowSizeText = "100";

    [ObservableProperty]
    private string analysisStateWindowSizeText = "30";

    [ObservableProperty]
    private string analysisHoldFramesText = "0";

    [ObservableProperty]
    private string sopWindowMsText = "1500";

    [ObservableProperty]
    private string sopMinScoreQ1000Text = "450";

    [ObservableProperty]
    private string sopMinVisibleRatioQ1000Text = "600";

    [ObservableProperty]
    private bool ocrEnabled;

    [ObservableProperty]
    private string ocrRoiXText = "0";

    [ObservableProperty]
    private string ocrRoiYText = "0";

    [ObservableProperty]
    private string ocrRoiWidthText = "200";

    [ObservableProperty]
    private string ocrRoiHeightText = "40";

    [ObservableProperty]
    private bool enableRecording;

    [ObservableProperty]
    private string recordingRootDirectoryText = "D:/video";

    [ObservableProperty]
    private string recordingSegmentMinutesText = "5";

    [ObservableProperty]
    private string selectedRecordingContainerFormat = "mkv";

    [ObservableProperty]
    private string selectedRecordingVideoEncoder = "h264_nvenc";

    [ObservableProperty]
    private string recordingBitrateMbpsText = "8";

    [ObservableProperty]
    private string deviceDiscoveryStatus = string.Empty;

    [ObservableProperty]
    private string selectedDeviceSummary = string.Empty;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    public bool UsesDeviceIdentifiers => !string.Equals(SelectedProviderId, CameraProviderIds.OpenCv, StringComparison.OrdinalIgnoreCase);
    public bool HasDiscoveredDevices => AvailableDevices.Count > 0;
    public string ProviderDisplayName => _cameraProviders.GetDisplayName(SelectedProviderId);
    public string SelectorText => UsesDeviceIdentifiers
        ? (!string.IsNullOrWhiteSpace(DeviceIdText) ? DeviceIdText.Trim() : "Manual device id")
        : $"Index {CameraIndexText.Trim()}";
    public string SidebarTitle => string.IsNullOrWhiteSpace(Name) ? "Unnamed camera" : Name.Trim();
    public string SidebarSubtitle => ProviderDisplayName;
    public string SidebarStateText => IsEnabled ? "Enabled" : "Disabled";
    public string StartupModeText => AutoStart ? "Auto start" : "Manual start";
    public string DeviceSelectionHint => UsesDeviceIdentifiers
        ? "Select a discovered device or enter a device id manually."
        : "OpenCV uses camera index selection. Device id is ignored.";

    partial void OnNameChanged(string value) => RaiseSidebarChanged();

    partial void OnIsEnabledChanged(bool value)
    {
        RaiseSidebarChanged();
        OnPropertyChanged(nameof(SidebarStateText));
    }

    partial void OnAutoStartChanged(bool value)
    {
        RaiseSidebarChanged();
        OnPropertyChanged(nameof(StartupModeText));
    }

    partial void OnSelectedProviderIdChanged(string value)
    {
        OnPropertyChanged(nameof(UsesDeviceIdentifiers));
        OnPropertyChanged(nameof(DeviceSelectionHint));
        OnPropertyChanged(nameof(ProviderDisplayName));
        ResetDeviceDiscovery(BuildPendingRefreshStatus(value));
        RaiseSidebarChanged();
    }

    partial void OnSelectedDeviceChanged(CameraDeviceInfo? value)
    {
        if (value != null)
        {
            DeviceIdText = value.DeviceId;
        }

        UpdateSelectedDeviceSummary();
    }

    partial void OnDeviceIdTextChanged(string value)
    {
        SyncSelectedDeviceToDeviceId();
        UpdateSelectedDeviceSummary();
        RaiseSidebarChanged();
    }

    partial void OnCameraIndexTextChanged(string value) => RaiseSidebarChanged();
    partial void OnEnableRecordingChanged(bool value) => RaiseSidebarChanged();

    [RelayCommand]
    private async Task RefreshDevices()
    {
        await RefreshDevicesCoreAsync();
    }

    public CameraProfile Duplicate()
    {
        var clone = ToCameraProfile();
        clone.Id = Guid.NewGuid().ToString("N");
        clone.Name = $"{clone.Name} Copy";
        clone.AutoStart = false;
        return clone.Normalize(1);
    }

    private CameraProfile ToCameraProfile()
    {
        var profile = _hiddenSettings.Clone();
        profile.Id = Id;
        profile.Name = Name;
        profile.Enabled = IsEnabled;
        profile.AutoStart = AutoStart;
        profile.ProviderId = SelectedProviderId;
        profile.CameraIndex = int.TryParse(CameraIndexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cameraIndex)
            ? cameraIndex
            : 0;
        profile.DeviceId = UsesDeviceIdentifiers ? DeviceIdText : string.Empty;
        profile.TargetFps = double.TryParse(TargetFpsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps)
            ? fps
            : 10;
        profile.Rotation = Rotation;
        profile.MirrorMode = MirrorMode;
        profile.UseSourcePtsForVideo = UseSourcePtsForVideo;
        profile.PrimaryTaskId = PrimaryTaskId;
        profile.SelectedSopProfileId = SelectedSopProfileId;
        profile.EnableSopAnalysis = EnableSopAnalysis;
        profile.OcrEnabled = OcrEnabled;
        profile.OcrRoiX = int.TryParse(OcrRoiXText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ocrX) ? ocrX : 0;
        profile.OcrRoiY = int.TryParse(OcrRoiYText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ocrY) ? ocrY : 0;
        profile.OcrRoiWidth = int.TryParse(OcrRoiWidthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ocrW) ? ocrW : 200;
        profile.OcrRoiHeight = int.TryParse(OcrRoiHeightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ocrH) ? ocrH : 40;
        profile.AnalysisFrameWindowSize = int.TryParse(AnalysisFrameWindowSizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frameWindow)
            ? frameWindow
            : 100;
        profile.AnalysisStateWindowSize = int.TryParse(AnalysisStateWindowSizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stateWindow)
            ? stateWindow
            : 30;
        profile.AnalysisHoldFrames = int.TryParse(AnalysisHoldFramesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var holdFrames)
            ? holdFrames
            : 0;
        profile.SopWindowMs = int.TryParse(SopWindowMsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sopWindowMs)
            ? sopWindowMs
            : 1500;
        profile.SopMinScoreQ1000 = int.TryParse(SopMinScoreQ1000Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sopMinScore)
            ? sopMinScore
            : 450;
        profile.SopMinVisibleRatioQ1000 = int.TryParse(SopMinVisibleRatioQ1000Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sopVisibleRatio)
            ? sopVisibleRatio
            : 600;
        profile.EnableCameraRecording = EnableRecording;
        profile.RecordingRootDirectory = RecordingRootDirectoryText;
        profile.RecordingSegmentMinutes = int.TryParse(RecordingSegmentMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var segmentMinutes)
            ? segmentMinutes
            : 5;
        profile.RecordingContainerFormat = SelectedRecordingContainerFormat;
        profile.RecordingVideoEncoder = SelectedRecordingVideoEncoder;
        profile.RecordingBitrateMbps = int.TryParse(RecordingBitrateMbpsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitrateMbps)
            ? bitrateMbps
            : 8;
        return profile;
    }

    public bool TryBuild(out CameraProfile profile, out string error)
    {
        error = string.Empty;
        profile = new CameraProfile();

        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "Camera name is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(SelectedProviderId))
        {
            error = "Please select a camera provider.";
            return false;
        }

        if (!int.TryParse(CameraIndexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cameraIndex) ||
            cameraIndex < 0)
        {
            error = "Camera index must be a non-negative integer.";
            return false;
        }

        if (!double.TryParse(TargetFpsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps) || fps <= 0)
        {
            error = "Target FPS must be greater than 0.";
            return false;
        }

        if (!int.TryParse(RecordingSegmentMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var segmentMinutes) ||
            segmentMinutes <= 0)
        {
            error = "Recording segment minutes must be greater than 0.";
            return false;
        }

        if (!int.TryParse(RecordingBitrateMbpsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitrateMbps) ||
            bitrateMbps <= 0)
        {
            error = "Recording bitrate must be greater than 0 Mbps.";
            return false;
        }

        if (!TryParsePositiveInt(AnalysisFrameWindowSizeText, out var analysisFrameWindowSize))
        {
            error = "Analysis frame window must be greater than 0.";
            return false;
        }

        if (!TryParsePositiveInt(AnalysisStateWindowSizeText, out var analysisStateWindowSize))
        {
            error = "Analysis state window must be greater than 0.";
            return false;
        }

        if (!int.TryParse(AnalysisHoldFramesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var analysisHoldFrames) ||
            analysisHoldFrames < 0)
        {
            error = "Analysis hold frames must be a non-negative integer.";
            return false;
        }

        if (!TryParsePositiveInt(SopWindowMsText, out var sopWindowMs))
        {
            error = "SOP window must be greater than 0 ms.";
            return false;
        }

        if (!TryParsePositiveInt(SopMinScoreQ1000Text, out var sopMinScoreQ1000))
        {
            error = "SOP min score must be greater than 0.";
            return false;
        }

        if (!TryParsePositiveInt(SopMinVisibleRatioQ1000Text, out var sopMinVisibleRatioQ1000))
        {
            error = "SOP visible ratio must be greater than 0.";
            return false;
        }

        profile = ToCameraProfile();
        profile.Name = Name.Trim();
        profile.ProviderId = SelectedProviderId.Trim();
        profile.CameraIndex = cameraIndex;
        profile.DeviceId = UsesDeviceIdentifiers ? DeviceIdText.Trim() : string.Empty;
        profile.RecordingRootDirectory = RecordingRootDirectoryText.Trim();
        profile.RecordingSegmentMinutes = segmentMinutes;
        profile.RecordingContainerFormat = string.IsNullOrWhiteSpace(SelectedRecordingContainerFormat)
            ? "mkv"
            : SelectedRecordingContainerFormat.Trim();
        profile.RecordingVideoEncoder = string.IsNullOrWhiteSpace(SelectedRecordingVideoEncoder)
            ? "hevc_nvenc"
            : SelectedRecordingVideoEncoder.Trim();
        profile.RecordingBitrateMbps = bitrateMbps;
        profile.AnalysisFrameWindowSize = analysisFrameWindowSize;
        profile.AnalysisStateWindowSize = analysisStateWindowSize;
        profile.AnalysisHoldFrames = analysisHoldFrames;
        profile.SopWindowMs = sopWindowMs;
        profile.SopMinScoreQ1000 = sopMinScoreQ1000;
        profile.SopMinVisibleRatioQ1000 = sopMinVisibleRatioQ1000;
        profile = profile.Normalize(1);
        return true;
    }

    private static bool TryParsePositiveInt(string value, out int result)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) && result > 0;
    }

    private async Task RefreshDevicesCoreAsync()
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
            CameraDiagnostics.Info(
                "camera-config",
                $"Enumerated {devices.Count} camera(s) for provider '{SelectedProviderId}'.");
        }
        catch (Exception ex)
        {
            DeviceDiscoveryStatus = $"{_cameraProviders.GetDisplayName(SelectedProviderId)} discovery failed.";
            ErrorMessage = $"Device enumeration failed: {ex.Message}";
            CameraDiagnostics.Error(
                "camera-config",
                $"Failed to enumerate cameras for provider '{SelectedProviderId}'.",
                ex);
        }

        OnPropertyChanged(nameof(HasDiscoveredDevices));
        UpdateSelectedDeviceSummary();
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
        if (string.Equals(providerId, CameraProviderIds.OpenCv, StringComparison.OrdinalIgnoreCase))
        {
            return "OpenCV uses camera index selection and usually does not need discovery.";
        }

        return $"Click refresh to discover {_cameraProviders.GetDisplayName(providerId)} cameras, or enter a device id manually.";
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

    private void SyncSelectedDeviceToDeviceId()
    {
        if (!UsesDeviceIdentifiers)
        {
            SelectedDevice = null;
            return;
        }

        var deviceId = DeviceIdText.Trim();
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

public sealed class SopProfileItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int StepCount { get; set; }
}
