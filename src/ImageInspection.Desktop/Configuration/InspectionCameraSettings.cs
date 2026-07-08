using System;
using System.Collections.Generic;
using System.Linq;

namespace VideoInferenceDemo.ImageInspection;

public sealed class InspectionCameraSettings
{
    public List<InspectionCameraProfile> Cameras { get; set; } = [];

    public string SelectedCameraId { get; set; } = string.Empty;

    public InspectionCameraProfile? ResolveSelectedCamera()
    {
        if (Cameras.Count == 0)
        {
            return null;
        }

        return Cameras.FirstOrDefault(camera => string.Equals(camera.Id, SelectedCameraId, StringComparison.OrdinalIgnoreCase))
               ?? Cameras[0];
    }

    public static InspectionCameraSettings CreateDefault()
    {
        var camera = InspectionCameraProfile.CreateDefault(1);
        return new InspectionCameraSettings
        {
            Cameras = [camera],
            SelectedCameraId = camera.Id
        };
    }
}
