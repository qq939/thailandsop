using System.Text.Json;

namespace VideoInferenceDemo;

public static class MediaPipeHandPayloadParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static HandLandmarksPayload ParseOrEmpty(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new HandLandmarksPayload(Array.Empty<HandLandmarkSet>());
        }

        var payload = JsonSerializer.Deserialize<HandPayloadDto>(payloadJson, JsonOptions);
        if (payload?.Hands == null || payload.Hands.Count == 0)
        {
            return new HandLandmarksPayload(Array.Empty<HandLandmarkSet>());
        }

        var hands = payload.Hands
            .Select(hand => new HandLandmarkSet(
                hand.Label ?? string.Empty,
                hand.Score,
                hand.Landmarks?
                    .Select(point => new HandLandmarkPoint(
                        point.Index,
                        point.X,
                        point.Y,
                        point.Z,
                        point.Score))
                    .ToArray()
                ?? Array.Empty<HandLandmarkPoint>()))
            .ToArray();

        return new HandLandmarksPayload(hands);
    }

    private sealed class HandPayloadDto
    {
        public List<HandDto>? Hands { get; init; }
    }

    private sealed class HandDto
    {
        public string? Label { get; init; }
        public float Score { get; init; }
        public List<LandmarkDto>? Landmarks { get; init; }
    }

    private sealed class LandmarkDto
    {
        public int Index { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        public float Z { get; init; }
        public float Score { get; init; }
    }
}
