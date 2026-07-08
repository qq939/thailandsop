namespace VideoInferenceDemo;

public sealed class MediaPipeHandTaskFactory : IVisionTaskFactory
{
    private readonly VisionWorkerHostFactory _workerHostFactory;

    public MediaPipeHandTaskFactory(VisionWorkerHostFactory? workerHostFactory = null)
    {
        _workerHostFactory = workerHostFactory ?? new VisionWorkerHostFactory();
    }

    public static MediaPipeHandTaskFactory Instance { get; } = new();

    public bool CanCreate(VisionTaskDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.RuntimeKind == VisionRuntimeKind.MediaPipe &&
               definition.TaskKind == VisionTaskKind.HandLandmarks;
    }

    public IVisionTask Create(VisionTaskDefinition definition, VisionTaskCreationContext context)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!CanCreate(definition))
        {
            throw new NotSupportedException(
                $"Vision task '{definition.Id}' ({definition.TaskKind}, {definition.RuntimeKind}) is not supported by {nameof(MediaPipeHandTaskFactory)}.");
        }

        var metadata = BuildMetadata(definition);
        var hostOptions = BuildHostOptions(definition, metadata);
        var workerHost = _workerHostFactory.Create(hostOptions);
        var workerClient = workerHost.CreateClient();
        return new MediaPipeHandLandmarkTask(definition, metadata, workerHost, workerClient);
    }

    private static MediaPipeHandTaskMetadata BuildMetadata(VisionTaskDefinition definition)
    {
        var taskFilePath = TryGetRequiredMetadata(definition, "taskFilePath");
        return new MediaPipeHandTaskMetadata
        {
            TaskFilePath = ResolveConfiguredPath(taskFilePath),
            WorkerKind = TryGetString(definition, "workerKind", "mediapipe_hand"),
            WorkerPythonPath = TryGetString(definition, "workerPythonPath", "python"),
            WorkerScriptPath = TryGetString(definition, "workerScriptPath", string.Empty),
            WorkerProtocol = TryGetProtocol(definition, "workerProtocol", VisionWorkerProtocolKind.NamedPipe),
            MaxHands = TryGetInt(definition, "maxHands", 2),
            MinHandDetectionConfidence = TryGetFloat(definition, "minHandDetectionConfidence", 0.5f),
            MinHandPresenceConfidence = TryGetFloat(definition, "minHandPresenceConfidence", 0.5f),
            MinTrackingConfidence = TryGetFloat(definition, "minTrackingConfidence", 0.5f),
            PreferredInputSize = TryGetInt(definition, "preferredInputSize", 640)
        };
    }

    private static VisionWorkerHostOptions BuildHostOptions(
        VisionTaskDefinition definition,
        MediaPipeHandTaskMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.WorkerScriptPath))
        {
            throw new InvalidOperationException(
                $"Vision task '{definition.Id}' is missing required metadata 'workerScriptPath'.");
        }

        var endpointName = BuildEndpointName(definition);
        return new VisionWorkerHostOptions
        {
            TaskId = definition.Id,
            TaskKind = definition.TaskKind,
            RuntimeKind = definition.RuntimeKind,
            WorkerKind = metadata.WorkerKind,
            ProtocolKind = metadata.WorkerProtocol,
            EndpointName = endpointName,
            ProcessStart = BuildProcessStartSpec(definition, metadata, endpointName),
            Metadata = BuildWorkerMetadata(metadata)
        };
    }

    private static WorkerProcessStartSpec BuildProcessStartSpec(
        VisionTaskDefinition definition,
        MediaPipeHandTaskMetadata metadata,
        string endpointName)
    {
        var scriptPath = metadata.WorkerScriptPath;
        var arguments = string.Join(
            ' ',
            Quote(scriptPath),
            "--endpoint", Quote(endpointName),
            "--task-file", Quote(metadata.TaskFilePath),
            "--worker-kind", Quote(metadata.WorkerKind));

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["VISION_TASK_ID"] = definition.Id,
            ["VISION_TASK_KIND"] = definition.TaskKind.ToString(),
            ["VISION_RUNTIME_KIND"] = definition.RuntimeKind.ToString()
        };

        return new WorkerProcessStartSpec
        {
            FileName = ResolvePythonExecutable(metadata),
            Arguments = arguments,
            WorkingDirectory = ResolveWorkingDirectory(scriptPath),
            EnvironmentVariables = environment
        };
    }

    private static IReadOnlyDictionary<string, string> BuildWorkerMetadata(MediaPipeHandTaskMetadata metadata)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["taskFilePath"] = metadata.TaskFilePath,
            ["workerPythonPath"] = metadata.WorkerPythonPath,
            ["maxHands"] = metadata.MaxHands.ToString(),
            ["minHandDetectionConfidence"] = metadata.MinHandDetectionConfidence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["minHandPresenceConfidence"] = metadata.MinHandPresenceConfidence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["minTrackingConfidence"] = metadata.MinTrackingConfidence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["preferredInputSize"] = metadata.PreferredInputSize.ToString()
        };
    }

    private static string BuildEndpointName(VisionTaskDefinition definition)
    {
        var slug = new string(
            definition.Id
                .Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')
                .ToArray());

        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "vision-task";
        }

        return $"{slug}-{Guid.NewGuid():N}";
    }

    private static string ResolveWorkingDirectory(string scriptPath)
    {
        var directory = Path.GetDirectoryName(scriptPath);
        return string.IsNullOrWhiteSpace(directory)
            ? AppContext.BaseDirectory
            : directory;
    }

    private static string ResolvePythonExecutable(MediaPipeHandTaskMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.WorkerPythonPath))
        {
            return "python";
        }

        var configured = ResolveConfiguredPath(metadata.WorkerPythonPath);
        if (Path.IsPathRooted(configured))
        {
            return configured;
        }

        if (TryResolveFromBaseDirectoryOrAncestors(configured, out var resolved))
        {
            return resolved;
        }

        return configured;
    }

    private static string ResolveConfiguredPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var trimmed = value.Trim();
        var expanded = Environment.ExpandEnvironmentVariables(trimmed);
        if (!string.IsNullOrWhiteSpace(expanded) &&
            !string.Equals(expanded, trimmed, StringComparison.Ordinal))
        {
            return expanded;
        }

        if (TryResolveNamedEnvironmentVariable(trimmed, out var resolved))
        {
            return resolved;
        }

        return trimmed;
    }

    private static bool TryResolveNamedEnvironmentVariable(string value, out string resolved)
    {
        resolved = string.Empty;

        if (value.Length < 3 || value[0] != '%' || value[^1] != '%')
        {
            return false;
        }

        var variableName = value[1..^1].Trim();
        if (string.IsNullOrWhiteSpace(variableName))
        {
            return false;
        }

        resolved = Environment.GetEnvironmentVariable(variableName)
                   ?? Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.User)
                   ?? Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.Machine)
                   ?? string.Empty;

        return !string.IsNullOrWhiteSpace(resolved);
    }

    private static bool TryResolveFromBaseDirectoryOrAncestors(string relativePath, out string resolved)
    {
        resolved = string.Empty;

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.GetFullPath(Path.Combine(directory.FullName, relativePath));
            if (File.Exists(candidate))
            {
                resolved = candidate;
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }

    private static string TryGetRequiredMetadata(VisionTaskDefinition definition, string key)
    {
        if (definition.Metadata.TryGetValue(key, out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException(
            $"Vision task '{definition.Id}' is missing required metadata '{key}'.");
    }

    private static int TryGetInt(VisionTaskDefinition definition, string key, int fallback)
    {
        return definition.Metadata.TryGetValue(key, out var value) &&
               int.TryParse(value, out var parsed) &&
               parsed > 0
            ? parsed
            : fallback;
    }

    private static string TryGetString(VisionTaskDefinition definition, string key, string fallback)
    {
        return definition.Metadata.TryGetValue(key, out var value) &&
               !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;
    }

    private static float TryGetFloat(VisionTaskDefinition definition, string key, float fallback)
    {
        return definition.Metadata.TryGetValue(key, out var value) &&
               float.TryParse(value, out var parsed) &&
               parsed > 0
            ? parsed
            : fallback;
    }

    private static VisionWorkerProtocolKind TryGetProtocol(
        VisionTaskDefinition definition,
        string key,
        VisionWorkerProtocolKind fallback)
    {
        return definition.Metadata.TryGetValue(key, out var value) &&
               Enum.TryParse<VisionWorkerProtocolKind>(value, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}
