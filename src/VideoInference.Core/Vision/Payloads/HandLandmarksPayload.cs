namespace VideoInferenceDemo;

public sealed record HandLandmarkPoint(
    int Index,
    float X,
    float Y,
    float Z,
    float Score);

public sealed record HandLandmarkSet(
    string Handedness,
    float Score,
    IReadOnlyList<HandLandmarkPoint> Points);

public sealed class HandLandmarksPayload : VisionTaskPayload
{
    public HandLandmarksPayload(IReadOnlyList<HandLandmarkSet> hands)
    {
        Hands = hands ?? Array.Empty<HandLandmarkSet>();
    }

    public IReadOnlyList<HandLandmarkSet> Hands { get; }
}
