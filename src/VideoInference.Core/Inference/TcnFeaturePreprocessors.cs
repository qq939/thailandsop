using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VideoInferenceDemo;

public interface ITcnFeaturePreprocessor
{
    int FeatureDim { get; }
    bool TryBuild(FrameDetections input, out float[] features);
}

public sealed class TcnFeatureConfig
{
    public string Name { get; set; } = "det_topk";
    public string Version { get; set; } = "v1";
    public int TopK { get; set; } = 5;
    public bool IncludeClassId { get; set; } = true;
    public bool IncludeScore { get; set; } = true;
    public bool UseCenterSize { get; set; } = true;
    public bool Normalize { get; set; } = true;
    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public int FeatureDim => TopK * PerDetDim();

    [JsonIgnore]
    public bool IsUsable => Enabled && TopK > 0 && FeatureDim > 0;

    public TcnFeatureVersion ToVersion()
    {
        var json = JsonSerializer.Serialize(this);
        return new TcnFeatureVersion(Name, Version, FeatureDim, json);
    }

    public static TcnFeatureConfig Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<TcnFeatureConfig>(json);
                if (config != null)
                {
                    return config;
                }
            }
        }
        catch
        {
        }

        return new TcnFeatureConfig();
    }

    private int PerDetDim()
    {
        var dim = 0;
        if (IncludeClassId)
        {
            dim++;
        }
        if (IncludeScore)
        {
            dim++;
        }

        dim += 4;
        return dim;
    }
}

public sealed class TopKDetectionsPreprocessor : ITcnFeaturePreprocessor
{
    private readonly int _topK;
    private readonly bool _includeClassId;
    private readonly bool _includeScore;
    private readonly bool _useCenterSize;
    private readonly bool _normalize;
    private readonly int _perDetDim;

    public TopKDetectionsPreprocessor(TcnFeatureConfig config)
    {
        _topK = Math.Max(0, config.TopK);
        _includeClassId = config.IncludeClassId;
        _includeScore = config.IncludeScore;
        _useCenterSize = config.UseCenterSize;
        _normalize = config.Normalize;
        _perDetDim = 0;
        if (_includeClassId)
        {
            _perDetDim++;
        }
        if (_includeScore)
        {
            _perDetDim++;
        }
        _perDetDim += 4;
        FeatureDim = _topK * _perDetDim;
    }

    public int FeatureDim { get; }

    public bool TryBuild(FrameDetections input, out float[] features)
    {
        features = Array.Empty<float>();
        if (_topK <= 0)
        {
            return false;
        }

        features = new float[FeatureDim];
        if (input.Detections.Count == 0)
        {
            return true;
        }

        var width = input.Frame.Width > 0 ? input.Frame.Width : 1;
        var height = input.Frame.Height > 0 ? input.Frame.Height : 1;
        var sorted = input.Detections.Count == 1
            ? new[] { input.Detections[0] }
            : SortByScore(input.Detections);

        var count = Math.Min(_topK, sorted.Length);
        for (var i = 0; i < count; i++)
        {
            var det = sorted[i];
            var offset = i * _perDetDim;
            if (_includeClassId)
            {
                features[offset++] = det.ClassId;
            }
            if (_includeScore)
            {
                features[offset++] = det.Score;
            }

            var x1 = det.X1;
            var y1 = det.Y1;
            var x2 = det.X2;
            var y2 = det.Y2;
            if (_normalize)
            {
                x1 /= width;
                x2 /= width;
                y1 /= height;
                y2 /= height;
            }

            if (_useCenterSize)
            {
                var cx = (x1 + x2) * 0.5f;
                var cy = (y1 + y2) * 0.5f;
                var w = x2 - x1;
                var h = y2 - y1;
                features[offset++] = Clamp01(cx);
                features[offset++] = Clamp01(cy);
                features[offset++] = Clamp01(w);
                features[offset++] = Clamp01(h);
            }
            else
            {
                features[offset++] = Clamp01(x1);
                features[offset++] = Clamp01(y1);
                features[offset++] = Clamp01(x2);
                features[offset++] = Clamp01(y2);
            }
        }

        return true;
    }

    private static float Clamp01(float value)
    {
        if (value < 0f)
        {
            return 0f;
        }

        if (value > 1f)
        {
            return 1f;
        }

        return value;
    }

    private static DetectionEntity[] SortByScore(IReadOnlyList<DetectionEntity> detections)
    {
        var arr = new DetectionEntity[detections.Count];
        for (var i = 0; i < detections.Count; i++)
        {
            arr[i] = detections[i];
        }

        Array.Sort(arr, (a, b) => b.Score.CompareTo(a.Score));
        return arr;
    }
}
