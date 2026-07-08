using System;
using System.IO;
using System.Text.Json;

namespace VideoInferenceDemo;

public sealed class TcnInferenceConfig
{
    public bool Enabled { get; set; } = false;
    public string ModelPath { get; set; } = string.Empty;
    public string? ClassesPath { get; set; }
    public int WindowSize { get; set; } = 32;
    public int Stride { get; set; } = 4;
    public int QueueCapacity { get; set; } = 256;
    public bool ApplySoftmax { get; set; } = true;
    public string? InputName { get; set; }
    public string? OutputName { get; set; }
    public string? OrtNativeLibraryPath { get; set; }
    public string[]? OrtProviderOrder { get; set; }
    public int OrtDeviceId { get; set; }
    public bool OrtTensorRtFp16 { get; set; }
    public bool OrtTensorRtEngineCache { get; set; }
    public string? OrtTensorRtEngineCachePath { get; set; }

    public bool IsUsable => Enabled && WindowSize > 0 && Stride > 0;

    public static TcnInferenceConfig Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<TcnInferenceConfig>(json);
                if (config != null)
                {
                    return config;
                }
            }
        }
        catch
        {
        }

        return new TcnInferenceConfig();
    }
}
