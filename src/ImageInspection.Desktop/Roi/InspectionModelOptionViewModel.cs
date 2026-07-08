namespace VideoInferenceDemo.ImageInspection.Roi;

public sealed class InspectionModelOptionViewModel
{
    public InspectionModelOptionViewModel(string id, string name, ModelTaskType taskType)
    {
        Id = id;
        Name = name;
        TaskType = taskType;
    }

    public string Id { get; }

    public string Name { get; }

    public ModelTaskType TaskType { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Id)
        ? Name
        : $"{Name} ({TaskType})";
}
