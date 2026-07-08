using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoInferenceDemo.ImageInspection.Tasks;

public sealed partial class InspectionTaskDefinitionViewModel : ObservableObject
{
    public InspectionTaskDefinitionViewModel(InspectionTaskDefinition definition)
    {
        Id = definition.Id;
        Name = definition.Name;
        ActionType = definition.ActionType;
        Enabled = definition.Enabled;
        Description = definition.Description;
    }

    [ObservableProperty] private string id = string.Empty;
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string actionType = InspectionActionTypes.RoiInspection;
    [ObservableProperty] private bool enabled = true;
    [ObservableProperty] private string description = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "未命名任务定义" : Name;

    partial void OnIdChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    public InspectionTaskDefinition Build()
    {
        return new InspectionTaskDefinition
        {
            Id = Id,
            Name = Name,
            ActionType = ActionType,
            Enabled = Enabled,
            Description = Description
        };
    }
}
