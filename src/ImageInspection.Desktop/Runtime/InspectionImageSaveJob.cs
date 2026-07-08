using OpenCvSharp;

namespace VideoInferenceDemo.ImageInspection.Runtime;

internal sealed record InspectionImageSaveJob(
    InspectionCameraProfile Profile,
    Mat Image,
    IReadOnlyList<RoiDefinition> Rois,
    string ProductModel,
    string TaskId,
    string PositionNo,
    DateTimeOffset Timestamp,
    string ImagePath,
    IReadOnlyDictionary<string, string> RoiImagePaths);

internal sealed record InspectionImageSavePlan(
    int ExpectedRoiCount,
    string? ImagePath,
    IReadOnlyDictionary<string, string> RoiImagePaths)
{
    public static InspectionImageSavePlan Empty { get; } = new(0, null, new Dictionary<string, string>());
}
