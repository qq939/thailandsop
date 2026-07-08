using OpenCvSharp;

namespace VideoInferenceDemo.ImageInspection.Runtime;

public interface IProductPresenceInspectionGate
{
    ProductPresenceInspectionResult Evaluate(Mat image);
}

public sealed class DisabledProductPresenceInspectionGate : IProductPresenceInspectionGate
{
    public ProductPresenceInspectionResult Evaluate(Mat image) => ProductPresenceInspectionResult.Disabled;
}

public sealed record ProductPresenceInspectionResult(
    bool Enabled,
    bool IsAbsent,
    bool IsFailure,
    string ModelId,
    string ModelName,
    string SummaryText,
    IReadOnlyDictionary<string, string> Metadata)
{
    public bool ShouldSkipInspection => Enabled && (IsAbsent || IsFailure);

    public static ProductPresenceInspectionResult Disabled { get; } = new(
        false,
        false,
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        new Dictionary<string, string>());

    public static ProductPresenceInspectionResult Present(
        string modelId,
        string modelName,
        PresenceClassificationResult result,
        IReadOnlyDictionary<string, string> metrics) =>
        Create(modelId, modelName, result, isFailure: false, metrics);

    public static ProductPresenceInspectionResult Failure(string modelId, string modelName, string message) => new(
        true,
        true,
        true,
        modelId,
        modelName,
        $"产品有无：检测失败，{message}",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["presence.enabled"] = "true",
            ["presence.modelId"] = modelId,
            ["presence.modelName"] = modelName,
            ["presence.status"] = "failure",
            ["presence.error"] = message
        });

    public ProductPresenceInspectionResult ForSkippedCamera(string cameraId, string primaryCameraId)
    {
        if (!ShouldSkipInspection || string.Equals(cameraId, primaryCameraId, StringComparison.OrdinalIgnoreCase))
        {
            return this;
        }

        var summary = IsFailure
            ? "产品有无：主相机检测失败，已跳过划痕检测"
            : "产品有无：主相机判定无产品，已跳过划痕检测";
        var metadata = new Dictionary<string, string>(Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["presence.cameraId"] = cameraId,
            ["presence.primaryCameraId"] = primaryCameraId,
            ["presence.skippedByPrimaryCamera"] = "true"
        };

        return this with
        {
            SummaryText = summary,
            Metadata = metadata
        };
    }

    private static ProductPresenceInspectionResult Create(
        string modelId,
        string modelName,
        PresenceClassificationResult result,
        bool isFailure,
        IReadOnlyDictionary<string, string> metrics)
    {
        var status = result.IsAbsent ? "absent" : "present";
        var probability = result.IsAbsent ? result.AbsentProbability : result.PresentProbability;
        var summary = $"{result.SummaryText}，模型={modelId}";
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["presence.enabled"] = "true",
            ["presence.modelId"] = modelId,
            ["presence.modelName"] = modelName,
            ["presence.status"] = status,
            ["presence.decision"] = result.DecisionText,
            ["presence.presentClass"] = result.Metadata.PresentClass,
            ["presence.absentClass"] = result.Metadata.AbsentClass,
            ["presence.threshold"] = result.Metadata.ProbabilityThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["presence.presentProbability"] = result.PresentProbability.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            ["presence.absentProbability"] = result.AbsentProbability.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            ["presence.probability"] = probability.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
        };

        foreach (var item in metrics)
        {
            metadata[$"presence.{item.Key}"] = item.Value;
        }

        return new ProductPresenceInspectionResult(
            true,
            result.IsAbsent,
            isFailure,
            modelId,
            modelName,
            result.IsAbsent ? $"{summary}，已跳过划痕检测" : summary,
            metadata);
    }
}

public sealed class ProductPresenceInspectionGate : IProductPresenceInspectionGate, IDisposable
{
    private readonly string _modelConfigPath;
    private readonly IInspectionModelRuntime _modelRuntime;
    private readonly bool _ownsRuntime;

    public ProductPresenceInspectionGate(string modelConfigPath)
        : this(modelConfigPath, new InspectionModelRuntimeRegistry(modelConfigPath), ownsRuntime: true)
    {
    }

    public ProductPresenceInspectionGate(
        string modelConfigPath,
        IInspectionModelRuntime modelRuntime,
        bool ownsRuntime = false)
    {
        _modelConfigPath = modelConfigPath;
        _modelRuntime = modelRuntime;
        _ownsRuntime = ownsRuntime;
    }

    public ProductPresenceInspectionResult Evaluate(Mat image)
    {
        var model = ResolvePresenceModel();
        if (model == null)
        {
            return ProductPresenceInspectionResult.Disabled;
        }

        try
        {
            var execution = _modelRuntime.Execute(model.Id, image);
            if (execution.Payload is not PresenceClassificationPayload payload)
            {
                return ProductPresenceInspectionResult.Failure(
                    model.Id,
                    model.Name,
                    $"模型返回类型不是产品有无分类: {execution.TaskKind}");
            }

            return ProductPresenceInspectionResult.Present(
                execution.ModelId,
                execution.ModelName,
                payload.Result,
                execution.Metrics);
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Error("product-presence", $"Product presence classification failed for model '{model.Id}'.", ex);
            return ProductPresenceInspectionResult.Failure(model.Id, model.Name, ex.Message);
        }
    }

    public void Dispose()
    {
        if (_ownsRuntime && _modelRuntime is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private InspectionModelConfig? ResolvePresenceModel()
    {
        var settings = InspectionModelSettingsStorage.Load(_modelConfigPath);
        return settings.Models
            .Select((model, index) => (Model: model.Normalize(index + 1), Index: index))
            .Where(item => item.Model.Enabled && item.Model.TaskType == ModelTaskType.PresenceClassification)
            .OrderBy(item => item.Index)
            .Select(item => item.Model)
            .FirstOrDefault();
    }
}
