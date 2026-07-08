namespace VideoInferenceDemo.ImageInspection.Roi;

public sealed class InspectionRoiCameraOptionViewModel
{
    public InspectionRoiCameraOptionViewModel(string id, string name)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? id : name.Trim();
    }

    public string Id { get; }

    public string Name { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : $"{Name} ({Id})";
}
