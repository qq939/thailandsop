using System;
using System.Collections.Generic;
using System.Linq;

namespace VideoInferenceDemo;

public enum OrtExecutionProviderKind
{
    Cpu,
    Cuda,
    TensorRt
}

public static class OrtExecutionProviderParser
{
    public static bool TryParse(string? value, out OrtExecutionProviderKind provider)
    {
        provider = OrtExecutionProviderKind.Cpu;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "cpu":
                provider = OrtExecutionProviderKind.Cpu;
                return true;
            case "cuda":
                provider = OrtExecutionProviderKind.Cuda;
                return true;
            case "trt":
            case "tensorrt":
                provider = OrtExecutionProviderKind.TensorRt;
                return true;
            default:
                return false;
        }
    }

    public static IReadOnlyList<OrtExecutionProviderKind> ParseMany(IEnumerable<string>? values)
    {
        if (values == null)
        {
            return Array.Empty<OrtExecutionProviderKind>();
        }

        var providers = new List<OrtExecutionProviderKind>();
        foreach (var raw in values)
        {
            if (!TryParse(raw, out var provider))
            {
                continue;
            }

            if (!providers.Contains(provider))
            {
                providers.Add(provider);
            }
        }

        return providers;
    }

    public static IReadOnlyList<OrtExecutionProviderKind> ParseCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return Array.Empty<OrtExecutionProviderKind>();
        }

        return ParseMany(csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
