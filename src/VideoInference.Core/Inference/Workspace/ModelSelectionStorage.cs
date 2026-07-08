using System.Text.Json;

namespace VideoInferenceDemo;

public static class ModelSelectionStorage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string? LoadPreferredModelSourceId(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<ModelSelectionState>(File.ReadAllText(path), Options);
            var preferredModelSourceId = !string.IsNullOrWhiteSpace(state?.PreferredModelSourceId)
                ? state.PreferredModelSourceId
                : state?.SelectedModelId;
            return string.IsNullOrWhiteSpace(preferredModelSourceId) ? null : preferredModelSourceId;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static void SavePreferredModelSourceId(string path, string? preferredModelSourceId)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var state = new ModelSelectionState
            {
                PreferredModelSourceId = preferredModelSourceId ?? string.Empty,
                SelectedModelId = preferredModelSourceId ?? string.Empty
            };
            File.WriteAllText(path, JsonSerializer.Serialize(state, Options));
        }
        catch (IOException)
        {
        }
    }

    private sealed class ModelSelectionState
    {
        public string PreferredModelSourceId { get; set; } = string.Empty;
        public string SelectedModelId { get; set; } = string.Empty;
    }
}
