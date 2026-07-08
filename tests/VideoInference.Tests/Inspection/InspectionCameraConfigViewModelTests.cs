using VideoInferenceDemo.ImageInspection;
using VideoInferenceDemo.ImageInspection.Camera;

namespace VideoInferenceDemo.Tests.Inspection;

public sealed class InspectionCameraConfigViewModelTests
{
    [Fact]
    public void RemoveCamera_DoesNotRemoveLastCamera()
    {
        var camera = new InspectionCameraProfile
        {
            Id = "cam-1",
            Name = "Camera 1"
        };
        var viewModel = CreateViewModel(new InspectionCameraSettings
        {
            SelectedCameraId = camera.Id,
            Cameras = [camera]
        });

        Assert.False(viewModel.RemoveCameraCommand.CanExecute(null));
        viewModel.RemoveCameraCommand.Execute(null);

        Assert.Single(viewModel.Cameras);
        Assert.NotNull(viewModel.SelectedCamera);
        Assert.Equal("cam-1", viewModel.SelectedCamera!.Id);
    }

    [Fact]
    public void RemoveCamera_SelectsNeighborBeforeRemovingCurrentCamera()
    {
        var first = new InspectionCameraProfile
        {
            Id = "cam-1",
            Name = "Camera 1"
        };
        var second = new InspectionCameraProfile
        {
            Id = "cam-2",
            Name = "Camera 2"
        };
        var viewModel = CreateViewModel(new InspectionCameraSettings
        {
            SelectedCameraId = first.Id,
            Cameras = [first, second]
        });

        Assert.True(viewModel.RemoveCameraCommand.CanExecute(null));
        viewModel.RemoveCameraCommand.Execute(null);

        Assert.Single(viewModel.Cameras);
        Assert.Equal("cam-2", viewModel.SelectedCamera?.Id);
        Assert.False(viewModel.RemoveCameraCommand.CanExecute(null));
    }

    private static InspectionCameraConfigViewModel CreateViewModel(InspectionCameraSettings settings)
    {
        return new InspectionCameraConfigViewModel(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"),
            settings,
            new CameraProviderRegistry(Array.Empty<CameraProviderRegistration>()));
    }
}
