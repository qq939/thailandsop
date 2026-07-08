using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ImageBox;

namespace VideoInferenceDemo.ImageInspection;

public sealed partial class InspectionCameraSessionViewModel : ObservableObject
{
    public const string IdleStatusText = "\u5f85\u673a";
    public const string UntestedResultText = "\u672a\u68c0\u6d4b";

    public InspectionCameraSessionViewModel(InspectionCameraProfile profile, int ordinal)
    {
        Profile = profile.Normalize(ordinal);
        Id = Profile.Id;
        Name = string.IsNullOrWhiteSpace(Profile.Name) ? $"Camera {ordinal}" : Profile.Name.Trim();
        ProviderId = Profile.ProviderId;
        SourceDescription = string.IsNullOrWhiteSpace(Profile.DeviceId)
            ? $"Index {Profile.CameraIndex}"
            : Profile.DeviceId;
    }

    public InspectionCameraProfile Profile { get; }

    public string Id { get; }

    public string Name { get; }

    public string ProviderId { get; }

    public string SourceDescription { get; }

    public CameraTriggerMode TriggerMode => Profile.TriggerMode;

    [ObservableProperty] private ImageSource? frameImage;
    [ObservableProperty] private string frameImagePath = string.Empty;
    [ObservableProperty] private RoiListItemViewModel? selectedRoi;
    [ObservableProperty] private string statusText = IdleStatusText;
    [ObservableProperty] private string resultText = UntestedResultText;
    [ObservableProperty] private string taskDisplayText = "Task: - / Action: -";
    [ObservableProperty] private string recipeDisplayText = "Product: - / Position: -";
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool isRunning;

    public ObservableCollection<RoiListItemViewModel> RoiItems { get; } = [];

    public ObservableCollection<ImageOverlayItem> ResultOverlayItems { get; } = [];

    public void MarkIdle()
    {
        IsRunning = false;
        StatusText = IdleStatusText;
    }
}
