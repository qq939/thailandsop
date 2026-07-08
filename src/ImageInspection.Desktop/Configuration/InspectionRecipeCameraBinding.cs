using System;
using System.Collections.Generic;
using System.Linq;

namespace VideoInferenceDemo.ImageInspection;

public static class InspectionRecipeCameraBinding
{
    public static string NormalizeForCameraIds(InspectionRecipeEntry recipe, IReadOnlyList<string> cameraIds)
    {
        return NormalizeForCameraIds(recipe, cameraIds, out _);
    }

    public static string NormalizeForCameraIds(
        InspectionRecipeEntry recipe,
        IReadOnlyList<string> cameraIds,
        out bool changed)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        changed = false;

        var validCameraIds = cameraIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (validCameraIds.Length == 0)
        {
            return string.Empty;
        }

        if (recipe.ReferenceImagePathsByCameraId == null)
        {
            recipe.ReferenceImagePathsByCameraId = [];
            changed = true;
        }

        if (recipe.Rois == null)
        {
            recipe.Rois = [];
            changed = true;
        }

        if (recipe.AlignmentByCameraId == null)
        {
            recipe.AlignmentByCameraId = [];
            changed = true;
        }

        if (recipe.ModelBindings == null)
        {
            recipe.ModelBindings = [];
            changed = true;
        }

        if (recipe.Parameters == null)
        {
            recipe.Parameters = [];
            changed = true;
        }

        var fallbackCameraId = validCameraIds[0];
        if (!string.IsNullOrWhiteSpace(recipe.ReferenceImagePath) &&
            !recipe.ReferenceImagePathsByCameraId.ContainsKey(fallbackCameraId))
        {
            recipe.ReferenceImagePathsByCameraId[fallbackCameraId] = recipe.ReferenceImagePath.Trim();
            changed = true;
        }

        var validSet = validCameraIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var roi in recipe.Rois)
        {
            if (string.IsNullOrWhiteSpace(roi.CameraId) || !validSet.Contains(roi.CameraId.Trim()))
            {
                if (!string.Equals(roi.CameraId, fallbackCameraId, StringComparison.Ordinal))
                {
                    changed = true;
                }

                roi.CameraId = fallbackCameraId;
            }
            else
            {
                var trimmed = roi.CameraId.Trim();
                if (!string.Equals(roi.CameraId, trimmed, StringComparison.Ordinal))
                {
                    changed = true;
                }

                roi.CameraId = trimmed;
            }
        }

        foreach (var key in recipe.AlignmentByCameraId.Keys.ToList())
        {
            if (!validSet.Contains(key))
            {
                recipe.AlignmentByCameraId.Remove(key);
                changed = true;
            }
        }

        return fallbackCameraId;
    }

    public static string GetReferenceImagePath(InspectionRecipeEntry recipe, string cameraId)
    {
        if (recipe.ReferenceImagePathsByCameraId != null &&
            !string.IsNullOrWhiteSpace(cameraId) &&
            recipe.ReferenceImagePathsByCameraId.TryGetValue(cameraId.Trim(), out var path))
        {
            return path?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    public static void SetReferenceImagePath(InspectionRecipeEntry recipe, string cameraId, string? path)
    {
        if (string.IsNullOrWhiteSpace(cameraId))
        {
            return;
        }

        recipe.ReferenceImagePathsByCameraId ??= [];
        recipe.ReferenceImagePathsByCameraId[cameraId.Trim()] = path?.Trim() ?? string.Empty;
        recipe.ReferenceImagePath = recipe.ReferenceImagePathsByCameraId.Values
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
