namespace VideoInferenceDemo.ImageInspection.Roi;

public sealed class InspectionModelClassOptionViewModel
{
    public InspectionModelClassOptionViewModel(int id, string name)
    {
        Id = id;
        Name = name?.Trim() ?? string.Empty;
    }

    public int Id { get; }

    public string Name { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? $"#{Id}"
        : $"{Name} (#{Id})";
}
