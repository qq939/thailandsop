using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoInferenceDemo.ImageInspection.Tasks;

public sealed partial class InspectionTaskCameraBindingViewModel : ObservableObject
{
    public InspectionTaskCameraBindingViewModel(InspectionCameraProfile camera, bool isSelected)
    {
        CameraId = camera.Id;
        Name = string.IsNullOrWhiteSpace(camera.Name) ? camera.Id : camera.Name.Trim();
        SourceDescription = string.IsNullOrWhiteSpace(camera.DeviceId)
            ? $"Index {camera.CameraIndex}"
            : camera.DeviceId.Trim();
        IsHardwareCallbackCamera = InspectionTaskTriggerCompatibility.IsHardwareCallbackCamera(camera);
        IsSelected = isSelected;
    }

    public string CameraId { get; }

    public string Name { get; }

    public string SourceDescription { get; }

    public bool IsHardwareCallbackCamera { get; }

    [ObservableProperty] private bool isSelected;
}
