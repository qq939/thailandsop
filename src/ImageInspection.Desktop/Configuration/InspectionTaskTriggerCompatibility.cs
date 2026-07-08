using System;
using System.Collections.Generic;
using System.Linq;

namespace VideoInferenceDemo.ImageInspection;

public enum InspectionTaskCameraTriggerCompatibility
{
    NoCameras,
    Command,
    HardwareCallback,
    Mixed
}

public static class InspectionTaskTriggerCompatibility
{
    public static bool NormalizeTriggerModes(
        InspectionTaskSettings settings,
        IReadOnlyList<InspectionCameraProfile>? cameras)
    {
        if (cameras is not { Count: > 0 })
        {
            return false;
        }

        var changed = false;
        foreach (var instance in settings.Instances)
        {
            var compatibility = ResolveCompatibility(instance.CameraIds, cameras);
            if (compatibility == InspectionTaskCameraTriggerCompatibility.HardwareCallback &&
                instance.TriggerMode == InspectionTaskTriggerMode.TriggerCommand)
            {
                instance.TriggerMode = InspectionTaskTriggerMode.CameraCallback;
                changed = true;
            }
        }

        return changed;
    }

    public static InspectionTaskTriggerMode ResolveDefaultTriggerMode(IReadOnlyList<InspectionCameraProfile>? cameras)
    {
        return cameras is { Count: > 0 } &&
               ResolveCompatibility(cameras) == InspectionTaskCameraTriggerCompatibility.HardwareCallback
            ? InspectionTaskTriggerMode.CameraCallback
            : InspectionTaskTriggerMode.TriggerCommand;
    }

    public static InspectionTaskCameraTriggerCompatibility ResolveCompatibility(
        IEnumerable<string> cameraIds,
        IReadOnlyList<InspectionCameraProfile> cameras)
    {
        var selectedIds = cameraIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedIds.Count == 0)
        {
            return InspectionTaskCameraTriggerCompatibility.NoCameras;
        }

        var selectedCameras = cameras
            .Where(camera => selectedIds.Contains(camera.Id))
            .ToList();
        return ResolveCompatibility(selectedCameras);
    }

    public static InspectionTaskCameraTriggerCompatibility ResolveCompatibility(
        IReadOnlyList<InspectionCameraProfile> cameras)
    {
        if (cameras.Count == 0)
        {
            return InspectionTaskCameraTriggerCompatibility.NoCameras;
        }

        var hardwareCount = cameras.Count(IsHardwareCallbackCamera);
        return hardwareCount switch
        {
            0 => InspectionTaskCameraTriggerCompatibility.Command,
            var count when count == cameras.Count => InspectionTaskCameraTriggerCompatibility.HardwareCallback,
            _ => InspectionTaskCameraTriggerCompatibility.Mixed
        };
    }

    public static bool IsHardwareCallbackCamera(InspectionCameraProfile camera)
    {
        return string.Equals(camera.ProviderId, CameraProviderIds.HikRobot, StringComparison.OrdinalIgnoreCase) &&
               camera.TriggerMode == CameraTriggerMode.HardwareLine0;
    }

    public static string FormatTriggerMode(InspectionTaskTriggerMode triggerMode)
    {
        return triggerMode switch
        {
            InspectionTaskTriggerMode.TriggerCommand => "触发命令",
            InspectionTaskTriggerMode.CameraCallback => "相机回调",
            _ => triggerMode.ToString()
        };
    }
}
