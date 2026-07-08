using System.Windows.Media;

namespace VideoInferenceDemo.ImageInspection.Runtime;

public sealed record InspectionRuntimeFrameResult(
    InspectionTaskSessionViewModel Task,
    InspectionCameraSessionViewModel Camera,
    InspectionCycleResult Result,
    ImageSource? FrameImage,
    int ImageWidth,
    int ImageHeight,
    string StatusText,
    InspectionRuntimeTiming Timing,
    int SavedRoiCount,
    string? ImagePath,
    IReadOnlyDictionary<string, string> RoiImagePaths);
