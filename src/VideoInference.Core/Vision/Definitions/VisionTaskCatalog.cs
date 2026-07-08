using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace VideoInferenceDemo;

public static class VisionTaskCatalog
{
    public static IReadOnlyList<VisionTaskDefinition> Discover(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return Array.Empty<VisionTaskDefinition>();
        }

        var tasks = new List<VisionTaskDefinition>();
        tasks.AddRange(DiscoverModelBackedTasks(baseDirectory));
        tasks.AddRange(DiscoverSpecialTasks(baseDirectory));

        if (tasks.Count > 0)
        {
            return tasks
                .GroupBy(task => task.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }

        return Array.Empty<VisionTaskDefinition>();
    }

    public static IReadOnlyList<VisionTaskDefinition> DiscoverModelBackedTasks(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return Array.Empty<VisionTaskDefinition>();
        }

        var dlRoot = Path.Combine(baseDirectory, "DL");
        var models = ModelCatalog.Discover(dlRoot);
        if (models.Count == 0)
        {
            models = ModelCatalog.Discover(baseDirectory);
        }

        return ModelCatalogVisionTaskMapper.ToVisionTaskDefinitions(models);
    }

    public static IReadOnlyList<VisionTaskDefinition> DiscoverSpecialTasks(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return Array.Empty<VisionTaskDefinition>();
        }

        var dlRoot = Path.Combine(baseDirectory, "DL");
        if (!Directory.Exists(dlRoot))
        {
            return Array.Empty<VisionTaskDefinition>();
        }

        var tasks = new List<VisionTaskDefinition>();
        foreach (var directory in Directory.GetDirectories(dlRoot).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var taskConfigPath = Path.Combine(directory, "task.json");
            if (!File.Exists(taskConfigPath))
            {
                continue;
            }

            if (TryLoadSpecialTaskDefinition(directory, taskConfigPath, out var definition))
            {
                tasks.Add(definition);
            }
        }

        return tasks;
    }

    public static VisionTaskDefinition? FindById(IEnumerable<VisionTaskDefinition> tasks, string? id)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return tasks.FirstOrDefault(task => string.Equals(task.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryLoadSpecialTaskDefinition(string bundleDirectory, string taskConfigPath, out VisionTaskDefinition definition)
    {
        definition = null!;

        try
        {
            var json = File.ReadAllText(taskConfigPath);
            var file = JsonSerializer.Deserialize<SpecialTaskFile>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (file == null ||
                string.IsNullOrWhiteSpace(file.Id) ||
                string.IsNullOrWhiteSpace(file.DisplayName) ||
                string.IsNullOrWhiteSpace(file.TaskKind) ||
                string.IsNullOrWhiteSpace(file.RuntimeKind))
            {
                return false;
            }

            if (!TryParseTaskKind(file.TaskKind, out var taskKind) ||
                !TryParseRuntimeKind(file.RuntimeKind, out var runtimeKind))
            {
                return false;
            }

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (file.Metadata != null)
            {
                foreach (var pair in file.Metadata)
                {
                    metadata[pair.Key] = pair.Value;
                }
            }

            foreach (var pair in metadata.ToArray())
            {
                if (string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                if (!ShouldNormalizeMetadataPath(pair.Key, pair.Value))
                {
                    continue;
                }

                if (!Path.IsPathRooted(pair.Value))
                {
                    metadata[pair.Key] = Path.GetFullPath(Path.Combine(bundleDirectory, pair.Value));
                }
            }

            definition = new VisionTaskDefinition
            {
                Id = file.Id.Trim(),
                DisplayName = file.DisplayName.Trim(),
                TaskKind = taskKind,
                RuntimeKind = runtimeKind,
                BundlePath = bundleDirectory,
                ConfigPath = taskConfigPath,
                Metadata = new ReadOnlyDictionary<string, string>(metadata)
            };
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseTaskKind(string value, out VisionTaskKind taskKind)
    {
        var normalized = value.Trim().Replace('-', '_');
        return Enum.TryParse(normalized, ignoreCase: true, out taskKind);
    }

    private static bool TryParseRuntimeKind(string value, out VisionRuntimeKind runtimeKind)
    {
        var normalized = value.Trim().Replace('-', '_');
        return Enum.TryParse(normalized, ignoreCase: true, out runtimeKind);
    }

    private static bool ShouldNormalizeMetadataPath(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (string.Equals(key, "workerPythonPath", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (value.Contains('%', StringComparison.Ordinal))
        {
            return false;
        }

        return key.EndsWith("Path", StringComparison.OrdinalIgnoreCase) ||
               key.EndsWith("File", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SpecialTaskFile
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string TaskKind { get; set; } = string.Empty;
        public string RuntimeKind { get; set; } = string.Empty;
        public Dictionary<string, string>? Metadata { get; set; }
    }
}
