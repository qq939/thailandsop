using System;

namespace VideoInferenceDemo;

public sealed class TcnFeatureVersion
{
    public TcnFeatureVersion(string name, string version, int featureDim, string? configJson = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("name is required", nameof(name)) : name;
        Version = string.IsNullOrWhiteSpace(version) ? throw new ArgumentException("version is required", nameof(version)) : version;
        FeatureDim = featureDim > 0 ? featureDim : throw new ArgumentOutOfRangeException(nameof(featureDim));
        ConfigJson = configJson;
    }

    public string Name { get; }
    public string Version { get; }
    public int FeatureDim { get; }
    public string? ConfigJson { get; }

    public string Key => $"{Name}::{Version}";
}

public sealed class TcnFeatureEntry
{
    public TcnFeatureEntry(
        string runUuid,
        long runStartedUtcMs,
        string sourceKey,
        int frameIndex,
        long ptsMs,
        long featureVersionId,
        float[] features)
    {
        RunUuid = runUuid ?? string.Empty;
        RunStartedUtcMs = runStartedUtcMs;
        SourceKey = sourceKey ?? string.Empty;
        FrameIndex = frameIndex;
        PtsMs = ptsMs;
        FeatureVersionId = featureVersionId;
        Features = features ?? Array.Empty<float>();
    }

    public TcnFeatureEntry(
        string runUuid,
        long runStartedUtcMs,
        string sourceKey,
        int frameIndex,
        long ptsMs,
        TcnFeatureVersion featureVersion,
        float[] features)
        : this(runUuid, runStartedUtcMs, sourceKey, frameIndex, ptsMs, 0, features)
    {
        FeatureVersion = featureVersion ?? throw new ArgumentNullException(nameof(featureVersion));
    }

    public string RunUuid { get; }
    public long RunStartedUtcMs { get; }
    public string SourceKey { get; }
    public int FrameIndex { get; }
    public long PtsMs { get; }
    public long FeatureVersionId { get; }
    public TcnFeatureVersion? FeatureVersion { get; }
    public float[] Features { get; }

    public long CreatedUtcMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
