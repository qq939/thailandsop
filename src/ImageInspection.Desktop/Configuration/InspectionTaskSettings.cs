using System;
using System.Collections.Generic;
using System.Linq;

namespace VideoInferenceDemo.ImageInspection;

public sealed class InspectionTaskSettings
{
    public List<InspectionTaskDefinition> Definitions { get; set; } = [];

    public List<InspectionTaskInstance> Instances { get; set; } = [];

    public static InspectionTaskSettings CreateDefault(
        IReadOnlyList<InspectionCameraProfile>? cameras = null,
        InspectionRecipeEntry? seedRecipe = null)
    {
        var definition = new InspectionTaskDefinition
        {
            Id = string.IsNullOrWhiteSpace(seedRecipe?.TaskId) ? "appearance-check" : seedRecipe.TaskId.Trim(),
            Name = "Appearance Check",
            ActionType = InspectionActionTypes.RoiInspection
        };

        var instance = new InspectionTaskInstance
        {
            Id = "station-1-appearance",
            Name = "Station 1 Appearance",
            DefinitionId = definition.Id,
            StationId = "station-1",
            TriggerMode = InspectionTaskTriggerCompatibility.ResolveDefaultTriggerMode(cameras?.Where(camera => camera.Enabled).ToList()),
            CameraIds = cameras?.Where(camera => camera.Enabled).Select(camera => camera.Id).ToList() ?? []
        };

        return new InspectionTaskSettings
        {
            Definitions = [definition],
            Instances = [instance]
        };
    }
}

public enum InspectionTaskTriggerMode
{
    TriggerCommand,
    CameraCallback
}

public sealed class InspectionTaskDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Inspection Task";

    public string ActionType { get; set; } = InspectionActionTypes.RoiInspection;

    public bool Enabled { get; set; } = true;

    public string Description { get; set; } = string.Empty;

    public InspectionTaskDefinition Normalize(int ordinal)
    {
        var id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        return new InspectionTaskDefinition
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(Name) ? $"Inspection Task {Math.Max(1, ordinal)}" : Name.Trim(),
            ActionType = string.IsNullOrWhiteSpace(ActionType) ? InspectionActionTypes.RoiInspection : ActionType.Trim(),
            Enabled = Enabled,
            Description = Description?.Trim() ?? string.Empty
        };
    }
}

public sealed class InspectionTaskInstance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Station Inspection";

    public string DefinitionId { get; set; } = string.Empty;

    public string StationId { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public InspectionTaskTriggerMode TriggerMode { get; set; } = InspectionTaskTriggerMode.TriggerCommand;

    public List<string> CameraIds { get; set; } = [];

    public InspectionTaskInstance Normalize(int ordinal, IReadOnlyList<InspectionTaskDefinition> definitions)
    {
        var definitionId = DefinitionId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(definitionId) ||
            definitions.All(definition => !string.Equals(definition.Id, definitionId, StringComparison.OrdinalIgnoreCase)))
        {
            definitionId = definitions.FirstOrDefault()?.Id ?? string.Empty;
        }

        return new InspectionTaskInstance
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim(),
            Name = string.IsNullOrWhiteSpace(Name) ? $"Station {Math.Max(1, ordinal)} Inspection" : Name.Trim(),
            DefinitionId = definitionId,
            StationId = string.IsNullOrWhiteSpace(StationId) ? $"station-{Math.Max(1, ordinal)}" : StationId.Trim(),
            Enabled = Enabled,
            TriggerMode = Enum.IsDefined(TriggerMode) ? TriggerMode : InspectionTaskTriggerMode.TriggerCommand,
            CameraIds = CameraIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? []
        };
    }
}

public static class InspectionActionTypes
{
    public const string RoiInspection = "roi-inspection";

    public static IReadOnlyList<string> All { get; } = [RoiInspection];
}
