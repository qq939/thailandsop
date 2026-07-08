using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoInferenceDemo.ImageInspection.Tasks;

public sealed partial class InspectionTaskInstanceViewModel : ObservableObject
{
    public InspectionTaskInstanceViewModel(
        InspectionTaskInstance instance,
        IReadOnlyList<InspectionCameraProfile> cameras)
    {
        Id = instance.Id;
        Name = instance.Name;
        DefinitionId = instance.DefinitionId;
        StationId = instance.StationId;
        Enabled = instance.Enabled;
        TriggerMode = instance.TriggerMode;

        var selectedCameraIds = instance.CameraIds.ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        foreach (var camera in cameras)
        {
            var binding = new InspectionTaskCameraBindingViewModel(camera, selectedCameraIds.Contains(camera.Id));
            binding.PropertyChanged += OnCameraBindingPropertyChanged;
            CameraBindings.Add(binding);
        }

        ApplyTriggerModeRecommendation();
    }

    [ObservableProperty] private string id = string.Empty;
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string definitionId = string.Empty;
    [ObservableProperty] private string stationId = string.Empty;
    [ObservableProperty] private bool enabled = true;
    [ObservableProperty] private InspectionTaskTriggerMode triggerMode = InspectionTaskTriggerMode.TriggerCommand;
    [ObservableProperty] private string triggerModeHint = string.Empty;

    public ObservableCollection<InspectionTaskCameraBindingViewModel> CameraBindings { get; } = [];

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;

    partial void OnIdChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    partial void OnTriggerModeChanged(InspectionTaskTriggerMode value)
    {
        if (value == InspectionTaskTriggerMode.TriggerCommand && SelectedCamerasAreAllHardwareCallback())
        {
            TriggerMode = InspectionTaskTriggerMode.CameraCallback;
            return;
        }

        UpdateTriggerModeHint();
    }

    public InspectionTaskInstance Build()
    {
        return new InspectionTaskInstance
        {
            Id = Id,
            Name = Name,
            DefinitionId = DefinitionId,
            StationId = StationId,
            Enabled = Enabled,
            TriggerMode = TriggerMode,
            CameraIds = CameraBindings
                .Where(binding => binding.IsSelected)
                .Select(binding => binding.CameraId)
                .ToList()
        };
    }

    private void OnCameraBindingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InspectionTaskCameraBindingViewModel.IsSelected))
        {
            ApplyTriggerModeRecommendation();
        }
    }

    private void ApplyTriggerModeRecommendation()
    {
        var selected = CameraBindings.Where(binding => binding.IsSelected).ToList();
        if (selected.Count > 0 && selected.All(binding => binding.IsHardwareCallbackCamera))
        {
            TriggerMode = InspectionTaskTriggerMode.CameraCallback;
        }

        UpdateTriggerModeHint();
    }

    private bool SelectedCamerasAreAllHardwareCallback()
    {
        var selected = CameraBindings.Where(binding => binding.IsSelected).ToList();
        return selected.Count > 0 && selected.All(binding => binding.IsHardwareCallbackCamera);
    }

    private void UpdateTriggerModeHint()
    {
        var selected = CameraBindings.Where(binding => binding.IsSelected).ToList();
        if (selected.Count == 0)
        {
            TriggerModeHint = string.Empty;
            return;
        }

        var hardwareCount = selected.Count(binding => binding.IsHardwareCallbackCamera);
        TriggerModeHint = hardwareCount switch
        {
            0 => TriggerMode == InspectionTaskTriggerMode.CameraCallback
                ? "当前绑定相机通常使用触发命令。"
                : string.Empty,
            var count when count == selected.Count => "已根据硬触发相机使用相机回调。",
            _ => "同一任务不能混用硬触发相机和命令式取图相机。"
        };
    }
}
