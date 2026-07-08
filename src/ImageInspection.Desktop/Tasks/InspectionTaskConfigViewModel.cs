using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VideoInferenceDemo.ImageInspection.Tasks;

public sealed partial class InspectionTaskConfigViewModel : ObservableObject
{
    private readonly string _taskConfigPath;
    private readonly string _modelConfigPath;
    private readonly IReadOnlyList<InspectionCameraProfile> _cameras;
    private readonly ImageInspection.Runtime.InspectionModelRuntimeRegistry _modelRuntimeRegistry;

    public InspectionTaskConfigViewModel(
        string taskConfigPath,
        string modelConfigPath,
        InspectionTaskSettings settings,
        InspectionModelSettings modelSettings,
        IReadOnlyList<InspectionCameraProfile> cameras)
    {
        _taskConfigPath = taskConfigPath;
        _modelConfigPath = modelConfigPath;
        _cameras = cameras;
        _modelRuntimeRegistry = new ImageInspection.Runtime.InspectionModelRuntimeRegistry(modelConfigPath);
        ActionTypes = InspectionActionTypes.All;
        TaskTriggerModes =
        [
            new(InspectionTaskTriggerMode.TriggerCommand, InspectionTaskTriggerCompatibility.FormatTriggerMode(InspectionTaskTriggerMode.TriggerCommand)),
            new(InspectionTaskTriggerMode.CameraCallback, InspectionTaskTriggerCompatibility.FormatTriggerMode(InspectionTaskTriggerMode.CameraCallback))
        ];
        ModelTaskTypes = Enum.GetValues<ModelTaskType>();
        RuntimeKinds = Enum.GetValues<VisionRuntimeKind>();
        DeviceKinds = Enum.GetValues<InferenceDeviceKind>();

        foreach (var definition in settings.Definitions)
        {
            Definitions.Add(new InspectionTaskDefinitionViewModel(definition));
        }

        foreach (var instance in settings.Instances)
        {
            Instances.Add(new InspectionTaskInstanceViewModel(instance, _cameras));
        }

        foreach (var model in modelSettings.Models)
        {
            Models.Add(new InspectionModelConfigViewModel(model));
        }

        SelectedDefinition = Definitions.FirstOrDefault();
        SelectedInstance = Instances.FirstOrDefault();
        SelectedModel = Models.FirstOrDefault();
    }

    public ObservableCollection<InspectionTaskDefinitionViewModel> Definitions { get; } = [];

    public ObservableCollection<InspectionTaskInstanceViewModel> Instances { get; } = [];

    public ObservableCollection<InspectionModelConfigViewModel> Models { get; } = [];

    public IReadOnlyList<string> ActionTypes { get; }

    public bool HasMultipleActionTypes => ActionTypes.Count > 1;

    public IReadOnlyList<InspectionTaskTriggerModeOption> TaskTriggerModes { get; }

    public IReadOnlyList<ModelTaskType> ModelTaskTypes { get; }

    public IReadOnlyList<VisionRuntimeKind> RuntimeKinds { get; }

    public IReadOnlyList<InferenceDeviceKind> DeviceKinds { get; }

    [ObservableProperty] private InspectionTaskDefinitionViewModel? selectedDefinition;
    [ObservableProperty] private InspectionTaskInstanceViewModel? selectedInstance;
    [ObservableProperty] private InspectionModelConfigViewModel? selectedModel;
    [ObservableProperty] private string modelEnvironmentStatus = "Model runtime has not been checked.";
    [ObservableProperty] private bool saveSucceeded;

    [RelayCommand]
    private void AddDefinition()
    {
        var ordinal = Definitions.Count + 1;
        var definition = new InspectionTaskDefinitionViewModel(new InspectionTaskDefinition
        {
            Id = $"task-definition-{ordinal}",
            Name = $"Inspection Task {ordinal}",
            ActionType = InspectionActionTypes.RoiInspection
        });
        Definitions.Add(definition);
        SelectedDefinition = definition;
    }

    [RelayCommand]
    private void RemoveDefinition(InspectionTaskDefinitionViewModel? definition)
    {
        definition ??= SelectedDefinition;
        if (definition == null || Definitions.Count <= 1)
        {
            return;
        }

        var removedId = definition.Id;
        var index = Definitions.IndexOf(definition);
        Definitions.Remove(definition);
        SelectedDefinition = Definitions[Math.Max(0, Math.Min(index, Definitions.Count - 1))];

        foreach (var instance in Instances.Where(instance => string.Equals(instance.DefinitionId, removedId, StringComparison.OrdinalIgnoreCase)))
        {
            instance.DefinitionId = SelectedDefinition.Id;
        }
    }

    [RelayCommand]
    private void AddInstance()
    {
        var ordinal = Instances.Count + 1;
        var instance = new InspectionTaskInstanceViewModel(
            new InspectionTaskInstance
            {
                Id = $"station-{ordinal}-task",
                Name = $"Station {ordinal} Inspection",
                StationId = $"station-{ordinal}",
                DefinitionId = SelectedDefinition?.Id ?? Definitions.FirstOrDefault()?.Id ?? string.Empty,
                TriggerMode = InspectionTaskTriggerCompatibility.ResolveDefaultTriggerMode(_cameras.Where(camera => camera.Enabled).ToList()),
                CameraIds = _cameras.Where(camera => camera.Enabled).Select(camera => camera.Id).ToList()
            },
            _cameras);
        Instances.Add(instance);
        SelectedInstance = instance;
    }

    [RelayCommand]
    private void RemoveInstance(InspectionTaskInstanceViewModel? instance)
    {
        instance ??= SelectedInstance;
        if (instance == null || Instances.Count <= 1)
        {
            return;
        }

        var index = Instances.IndexOf(instance);
        Instances.Remove(instance);
        SelectedInstance = Instances[Math.Max(0, Math.Min(index, Instances.Count - 1))];
    }

    [RelayCommand]
    private void RefreshModelsFromDl()
    {
        var existingIds = Models
            .Select(model => model.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = new InspectionModelSettings
        {
            Models = Models.Select(model => model.Build()).ToList()
        };
        var discovered = InspectionModelSettings.DiscoverDlModels(AppContext.BaseDirectory);
        var firstNewModel = discovered.FirstOrDefault(model => !existingIds.Contains(model.Id));
        var merged = InspectionModelSettings.MergeDiscoveredModels(current, discovered);
        ReloadModels(merged, firstNewModel?.Id);
        SaveModels();
        if (discovered.Count == 0)
        {
            ModelEnvironmentStatus = $"未在 DL 目录发现模型：{Path.Combine(AppContext.BaseDirectory, "DL")}";
            return;
        }

        var names = string.Join(", ", discovered.Select(model => $"{model.DisplayName}({model.TaskTypeDisplay})"));
        ModelEnvironmentStatus = firstNewModel == null
            ? $"已刷新 DL 模型 {discovered.Count} 个：{names}"
            : $"已新增并选中模型：{firstNewModel.DisplayName}；DL 目录共 {discovered.Count} 个：{names}";
    }

    [RelayCommand]
    private void RemoveModel(InspectionModelConfigViewModel? model)
    {
        model ??= SelectedModel;
        if (model == null || Models.Count <= 1)
        {
            return;
        }

        var index = Models.IndexOf(model);
        Models.Remove(model);
        SelectedModel = Models[Math.Max(0, Math.Min(index, Models.Count - 1))];
    }

    private CancellationTokenSource? _probeCts;

    public void CancelProbe()
    {
        _probeCts?.Cancel();
        _probeCts = null;
    }

    [RelayCommand]
    private async Task CheckModelEnvironmentAsync()
    {
        if (SelectedModel == null)
        {
            ModelEnvironmentStatus = "Please select a model first.";
            return;
        }

        SaveModels();
        ModelEnvironmentStatus = $"正在检查模型环境: {SelectedModel.DisplayName} ...";
        var model = SelectedModel.Build().Normalize(Models.IndexOf(SelectedModel) + 1);

        // 取消前一次探测（如果仍在运行）
        _probeCts?.Cancel();
        _probeCts = new CancellationTokenSource();
        var cts = _probeCts;

        try
        {
            const int timeoutSeconds = 60;
            var probeTask = Task.Run(() => _modelRuntimeRegistry.Probe(model), cts.Token);
            var completedTask = await Task.WhenAny(probeTask, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cts.Token));

            if (completedTask == probeTask)
            {
                var result = await probeTask;
                SelectedModel.RefreshDiagnostics();
                ModelEnvironmentStatus =
                    $"{(result.Success ? "OK" : "NG")} {result.ModelName} / 自动设备: {result.Message}";
            }
            else
            {
                ModelEnvironmentStatus =
                    $"NG {SelectedModel.DisplayName} / 自动设备: 检查超时（{timeoutSeconds}s），模型环境无响应。请确认 GPU 驱动、ONNX Runtime 环境正常。";
            }
        }
        catch (OperationCanceledException)
        {
            ModelEnvironmentStatus = $"已取消: {SelectedModel.DisplayName}";
        }
        catch (Exception ex)
        {
            ModelEnvironmentStatus = $"NG {SelectedModel.DisplayName} / 自动设备: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task BuildTensorRtCacheAsync()
    {
        if (SelectedModel == null)
        {
            ModelEnvironmentStatus = "Please select a model first.";
            return;
        }

        var model = SelectedModel.Build().Normalize(Models.IndexOf(SelectedModel) + 1);
        if (model.TaskType == ModelTaskType.OcrPipeline)
        {
            ModelEnvironmentStatus = $"OCR 管线模型不使用 TensorRT cache：{model.Name}";
            return;
        }

        if (string.IsNullOrWhiteSpace(model.ModelPath) || !File.Exists(model.ModelPath))
        {
            ModelEnvironmentStatus = $"NG {model.Name}: ONNX file does not exist: {model.ModelPath}";
            return;
        }

        if (model.InputWidth <= 0 || model.InputHeight <= 0)
        {
            ModelEnvironmentStatus = $"NG {model.Name}: model.json must declare inputWidth/inputHeight.";
            return;
        }

        SaveModels();
        var cacheDirectory = Path.Combine(AppContext.BaseDirectory, "trt-cache", model.Id);
        ModelEnvironmentStatus = $"Building TensorRT cache: {model.Name} -> {cacheDirectory}";

        var result = await TensorRtCacheBuilder.BuildCacheAsync(
            model.ModelPath,
            cacheDirectory,
            deviceId: 0,
            fp16: true,
            inputWidth: model.InputWidth,
            inputHeight: model.InputHeight);
        ModelEnvironmentStatus = result.Success
            ? $"OK {model.Name}: {result.Message} {result.CacheDirectory}"
            : $"NG {model.Name}: {result.Message}";
        SelectedModel.RefreshDiagnostics();
    }

    private static bool HasTensorRtCache(InspectionModelConfig model, out string cacheDirectory)
    {
        cacheDirectory = Path.Combine(AppContext.BaseDirectory, "trt-cache", model.Id);
        return Directory.Exists(cacheDirectory) &&
               Directory.EnumerateFiles(cacheDirectory, "*", SearchOption.AllDirectories).Any();
    }

    [RelayCommand]
    private void Save()
    {
        InspectionTaskSettingsStorage.Save(
            _taskConfigPath,
            new InspectionTaskSettings
            {
                Definitions = Definitions.Select(definition => definition.Build()).ToList(),
                Instances = Instances.Select(instance => instance.Build()).ToList()
            },
            _cameras);

        SaveModels();
        SaveSucceeded = true;
    }

    private void SaveModels()
    {
        InspectionModelSettingsStorage.Save(
            _modelConfigPath,
            new InspectionModelSettings
            {
                Models = Models.Select(model => model.Build()).ToList()
            });
    }

    private void ReloadModels(InspectionModelSettings settings, string? preferredModelId = null)
    {
        var selectedId = string.IsNullOrWhiteSpace(preferredModelId)
            ? SelectedModel?.Id
            : preferredModelId.Trim();
        Models.Clear();
        foreach (var model in settings.Models)
        {
            Models.Add(new InspectionModelConfigViewModel(model));
        }

        SelectedModel = Models.FirstOrDefault(model => string.Equals(model.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                        ?? Models.FirstOrDefault();
    }
}

public sealed record InspectionTaskTriggerModeOption(InspectionTaskTriggerMode Mode, string DisplayName);
