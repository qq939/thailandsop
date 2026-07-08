namespace VideoInferenceDemo;

public static class FingerprintRuntimeModuleSelector
{
    public static IReadOnlyList<FingerprintModuleOptions> SelectRuntimeModules(
        IEnumerable<FingerprintModuleOptions>? modules,
        IEnumerable<SopProfile>? sopProfiles)
    {
        var boundModuleIds = (sopProfiles ?? Array.Empty<SopProfile>())
            .Select(item => item?.FingerprintModuleId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return (modules ?? Array.Empty<FingerprintModuleOptions>())
            .Where(item => item != null)
            .Select(item =>
            {
                var normalized = item.Normalize();
                if (boundModuleIds.Contains(normalized.Id))
                {
                    normalized.Enabled = true;
                }

                return normalized;
            })
            .ToList();
    }
}
