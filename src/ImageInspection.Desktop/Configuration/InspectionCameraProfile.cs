using System;

namespace VideoInferenceDemo.ImageInspection;

public sealed class InspectionCameraProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Camera 1";

    public bool Enabled { get; set; } = true;

    public string ProviderId { get; set; } = CameraProviderIds.OpenCv;

    public int CameraIndex { get; set; }

    public string DeviceId { get; set; } = string.Empty;

    public string OpenCvSource { get; set; } = string.Empty;

    public string OpenCvBackend { get; set; } = string.Empty;

    public CameraTriggerMode TriggerMode { get; set; } = CameraTriggerMode.Software;

    public double TargetFps { get; set; } = 5;

    public bool SaveImages { get; set; } = true;

    public bool SaveRoiImages { get; set; }

    public string ImageSaveDirectory { get; set; } = "InspectionImages";

    public string ImageFileNamePattern { get; set; } = "{Timestamp:yyyyMMdd_HHmmssfff}.jpg";

    public InspectionCameraProfile Normalize(int ordinal)
    {
        return new InspectionCameraProfile
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim(),
            Name = string.IsNullOrWhiteSpace(Name) ? $"Camera {Math.Max(1, ordinal)}" : Name.Trim(),
            Enabled = Enabled,
            ProviderId = string.IsNullOrWhiteSpace(ProviderId) ? CameraProviderIds.OpenCv : ProviderId.Trim(),
            CameraIndex = Math.Max(0, CameraIndex),
            DeviceId = DeviceId?.Trim() ?? string.Empty,
            OpenCvSource = OpenCvSource?.Trim() ?? string.Empty,
            OpenCvBackend = OpenCvBackend?.Trim() ?? string.Empty,
            TriggerMode = Enum.IsDefined(TriggerMode) ? TriggerMode : CameraTriggerMode.Software,
            TargetFps = TargetFps > 0 ? TargetFps : 5,
            SaveImages = SaveImages,
            SaveRoiImages = SaveRoiImages,
            ImageSaveDirectory = string.IsNullOrWhiteSpace(ImageSaveDirectory) ? "InspectionImages" : ImageSaveDirectory.Trim(),
            ImageFileNamePattern = string.IsNullOrWhiteSpace(ImageFileNamePattern)
                ? "{Timestamp:yyyyMMdd_HHmmssfff}.jpg"
                : ImageFileNamePattern.Trim()
        };
    }

    public static InspectionCameraProfile CreateDefault(int ordinal)
    {
        return new InspectionCameraProfile().Normalize(Math.Max(1, ordinal));
    }

    public CameraOpenOptions BuildOpenOptions()
    {
        return new CameraOpenOptions(
                ProviderId,
                CameraIndex,
                DeviceId,
                TargetFps,
                OpenCvSource,
                OpenCvBackend,
                TriggerMode)
            .Normalize();
    }
}
