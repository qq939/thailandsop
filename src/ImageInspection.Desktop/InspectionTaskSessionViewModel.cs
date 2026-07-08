using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoInferenceDemo.ImageInspection;

public enum InspectionTriggerStatusKind
{
    Disabled,
    Waiting,
    Received,
    Processing,
    CompletedOk,
    CompletedNg,
    CompletedWarning,
    Error
}

public sealed partial class InspectionTaskSessionViewModel : ObservableObject
{
    private const string ProcessingText = "\u5904\u7406\u4e2d";
    private const string UntestedText = "\u672a\u68c0\u6d4b";
    private const string EmptyDetailText = "\u65e0\u68c0\u6d4b\u660e\u7ec6";
    private static readonly Brush NeutralBackground = CreateBrush(0xE9, 0xEF, 0xF5);
    private static readonly Brush NeutralForeground = CreateBrush(0x20, 0x32, 0x43);
    private static readonly Brush OkBackground = CreateBrush(0x12, 0x8A, 0x55);
    private static readonly Brush NgBackground = CreateBrush(0xC4, 0x2B, 0x2B);
    private static readonly Brush WarningBackground = CreateBrush(0xB7, 0x79, 0x1F);
    private static readonly Brush ActiveBackground = CreateBrush(0x1F, 0x6F, 0xB2);
    private static readonly Brush LightForeground = CreateBrush(0xFF, 0xFF, 0xFF);
    private readonly Dictionary<string, CameraInspectionSummarySnapshot> cameraInspectionSummaries = new(StringComparer.OrdinalIgnoreCase);
    private string currentInspectionTriggerId = string.Empty;

    public InspectionTaskSessionViewModel(
        string id,
        string name,
        string recipeTaskId,
        string productModel,
        string positionNo,
        string stationId,
        string actionType,
        string definitionId,
        InspectionTaskTriggerMode triggerMode = InspectionTaskTriggerMode.TriggerCommand)
    {
        Id = id;
        Name = name;
        RecipeTaskId = recipeTaskId;
        ProductModel = productModel;
        PositionNo = positionNo;
        StationId = stationId;
        ActionType = actionType;
        DefinitionId = definitionId;
        TriggerMode = triggerMode;
    }

    public string Id { get; }

    public string Name { get; }

    public string RecipeTaskId { get; }

    public string StationId { get; }

    public string ActionType { get; }

    public string DefinitionId { get; }

    public InspectionTaskTriggerMode TriggerMode { get; }

    public string TriggerModeText => InspectionTaskTriggerCompatibility.FormatTriggerMode(TriggerMode);

    public bool CanManualTrigger => TriggerMode == InspectionTaskTriggerMode.TriggerCommand;

    public bool CanTrigger => IsRunning && CanManualTrigger;

    [ObservableProperty] private string productModel;
    [ObservableProperty] private string positionNo;
    [ObservableProperty] private string referenceImagePath = string.Empty;
    [ObservableProperty] private string summaryMessage = "\u7b49\u5f85\u89e6\u53d1\u3002";
    [ObservableProperty] private string statusText = "\u5f85\u673a";
    [ObservableProperty] private string resultText = "\u672a\u68c0\u6d4b";
    [ObservableProperty] private Brush resultBackground = NeutralBackground;
    [ObservableProperty] private Brush resultForeground = NeutralForeground;
    [ObservableProperty] private string timingText = "\u6682\u65e0\u68c0\u6d4b\u8017\u65f6";
    [ObservableProperty] private InspectionTriggerStatusKind triggerStatusKind = InspectionTriggerStatusKind.Disabled;
    [ObservableProperty] private string triggerStatusText = "PLC\u89e6\u53d1\u672a\u542f\u7528";
    [ObservableProperty] private Brush triggerStatusBackground = NeutralBackground;
    [ObservableProperty] private Brush triggerStatusForeground = NeutralForeground;
    [ObservableProperty] private InspectionCameraSessionViewModel? selectedCamera;
    [ObservableProperty] private bool isRunning;

    public ObservableCollection<InspectionCameraSessionViewModel> Cameras { get; } = [];

    public string CameraSummary => $"{Cameras.Count} \u4e2a\u76f8\u673a";

    public string RecipeSummary => $"{ProductModel} / {RecipeTaskId} / {PositionNo}";

    public bool HasMultipleCameras => Cameras.Count > 1;

    public int GridColumns
    {
        get
        {
            var count = Cameras.Count;
            return count switch
            {
                <= 1 => 1,
                <= 4 => 2,
                _ => 4
            };
        }
    }

    partial void OnProductModelChanged(string value)
    {
        OnPropertyChanged(nameof(RecipeSummary));
    }

    partial void OnPositionNoChanged(string value)
    {
        OnPropertyChanged(nameof(RecipeSummary));
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanTrigger));
    }

    public void ApplyDecision(InspectionCycleDecision decision)
    {
        ResultText = FormatDecision(decision);
        ResultBackground = GetDecisionBackground(decision);
        ResultForeground = decision is InspectionCycleDecision.Ok or InspectionCycleDecision.Ng or InspectionCycleDecision.Warning
            ? LightForeground
            : NeutralForeground;
    }

    public void ApplyCameraInspectionResult(
        InspectionCameraSessionViewModel camera,
        InspectionCycleResult result,
        string formattedSummary,
        bool includeAllTaskCameras = true)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(result);

        var triggerId = CreateInspectionBatchId(result);
        if (!string.Equals(currentInspectionTriggerId, triggerId, StringComparison.Ordinal))
        {
            ClearInspectionBatch(triggerId);
        }

        var summary = string.IsNullOrWhiteSpace(formattedSummary)
            ? EmptyDetailText
            : formattedSummary.Trim();
        cameraInspectionSummaries[camera.Id] = new CameraInspectionSummarySnapshot(result.Decision, summary);

        var displayCameras = ResolveInspectionSummaryCameras(camera, includeAllTaskCameras);
        if (!includeAllTaskCameras || displayCameras.Length <= 1)
        {
            SummaryMessage = summary;
            ApplyDecision(result.Decision);
            return;
        }

        RebuildInspectionBatchSummary(displayCameras);
    }

    public void ClearInspectionBatch(string? triggerId = null)
    {
        currentInspectionTriggerId = string.IsNullOrWhiteSpace(triggerId) ? string.Empty : triggerId.Trim();
        cameraInspectionSummaries.Clear();
    }

    public void UpdateTriggerStatus(InspectionTriggerStatusKind kind, string? message = null)
    {
        TriggerStatusKind = kind;
        TriggerStatusText = string.IsNullOrWhiteSpace(message) ? FormatTriggerStatus(kind) : message.Trim();
        TriggerStatusBackground = GetTriggerStatusBackground(kind);
        TriggerStatusForeground = kind is InspectionTriggerStatusKind.Disabled or InspectionTriggerStatusKind.Waiting
            ? NeutralForeground
            : LightForeground;
    }

    public static string FormatDecision(InspectionCycleDecision decision)
    {
        return decision switch
        {
            InspectionCycleDecision.Ok => "OK",
            InspectionCycleDecision.Ng => "NG",
            InspectionCycleDecision.Warning => "WARNING",
            _ => UntestedText
        };
    }

    private void RebuildInspectionBatchSummary(IReadOnlyList<InspectionCameraSessionViewModel> displayCameras)
    {
        var okCount = 0;
        var ngCount = 0;
        var warningCount = 0;
        var unknownCount = 0;
        var pendingCount = 0;

        foreach (var camera in displayCameras)
        {
            if (!cameraInspectionSummaries.TryGetValue(camera.Id, out var snapshot))
            {
                pendingCount++;
                continue;
            }

            switch (snapshot.Decision)
            {
                case InspectionCycleDecision.Ok:
                    okCount++;
                    break;
                case InspectionCycleDecision.Ng:
                    ngCount++;
                    break;
                case InspectionCycleDecision.Warning:
                    warningCount++;
                    break;
                default:
                    unknownCount++;
                    break;
            }
        }

        var lines = new List<string>
        {
            BuildAggregateSummaryLine(displayCameras.Count, okCount, ngCount, warningCount, unknownCount, pendingCount)
        };

        foreach (var camera in displayCameras)
        {
            lines.Add(string.Empty);
            if (!cameraInspectionSummaries.TryGetValue(camera.Id, out var snapshot))
            {
                lines.Add($"{camera.Name} - {ProcessingText}");
                lines.Add($"  {ProcessingText}...");
                continue;
            }

            lines.Add($"{camera.Name} - {FormatDecision(snapshot.Decision)}");
            foreach (var line in SplitSummaryLines(snapshot.Summary))
            {
                lines.Add($"  {line}");
            }
        }

        SummaryMessage = string.Join(Environment.NewLine, lines);
        ApplyAggregateDecisionPresentation(okCount, ngCount, warningCount, unknownCount, pendingCount);
    }

    private void ApplyAggregateDecisionPresentation(
        int okCount,
        int ngCount,
        int warningCount,
        int unknownCount,
        int pendingCount)
    {
        if (ngCount > 0)
        {
            ApplyDecision(InspectionCycleDecision.Ng);
            return;
        }

        if (pendingCount > 0)
        {
            ResultText = ProcessingText;
            ResultBackground = ActiveBackground;
            ResultForeground = LightForeground;
            return;
        }

        if (warningCount > 0)
        {
            ApplyDecision(InspectionCycleDecision.Warning);
            return;
        }

        if (unknownCount > 0 || okCount == 0)
        {
            ApplyDecision(InspectionCycleDecision.Unknown);
            return;
        }

        ApplyDecision(InspectionCycleDecision.Ok);
    }

    private InspectionCameraSessionViewModel[] ResolveInspectionSummaryCameras(
        InspectionCameraSessionViewModel camera,
        bool includeAllTaskCameras)
    {
        if (!includeAllTaskCameras)
        {
            return [camera];
        }

        var cameras = new InspectionCameraSessionViewModel[Cameras.Count];
        Cameras.CopyTo(cameras, 0);
        if (cameras.Length == 0)
        {
            return [camera];
        }

        if (cameras.Any(item => string.Equals(item.Id, camera.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return cameras;
        }

        var expanded = cameras.ToList();
        expanded.Add(camera);
        return expanded.ToArray();
    }

    private static string BuildAggregateSummaryLine(
        int cameraCount,
        int okCount,
        int ngCount,
        int warningCount,
        int unknownCount,
        int pendingCount)
    {
        var aggregateText = ResolveAggregateSummaryText(okCount, ngCount, warningCount, unknownCount, pendingCount);
        var summary = $"\u6c47\u603b\uff1a{aggregateText}\uff08\u76f8\u673a={cameraCount}\uff0cOK={okCount}\uff0cNG={ngCount}";
        if (warningCount > 0)
        {
            summary += $"\uff0cWARNING={warningCount}";
        }

        if (unknownCount > 0)
        {
            summary += $"\uff0c{UntestedText}={unknownCount}";
        }

        return summary + $"\uff0c{ProcessingText}={pendingCount}\uff09";
    }

    private static string ResolveAggregateSummaryText(
        int okCount,
        int ngCount,
        int warningCount,
        int unknownCount,
        int pendingCount)
    {
        if (ngCount > 0)
        {
            return "NG";
        }

        if (pendingCount > 0)
        {
            return ProcessingText;
        }

        if (warningCount > 0)
        {
            return "WARNING";
        }

        if (unknownCount > 0 || okCount == 0)
        {
            return UntestedText;
        }

        return "OK";
    }

    private static string CreateInspectionBatchId(InspectionCycleResult result)
    {
        return string.IsNullOrWhiteSpace(result.TriggerId)
            ? result.TriggerTime.ToString("O")
            : result.TriggerId.Trim();
    }

    private static IEnumerable<string> SplitSummaryLines(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string FormatTriggerStatus(InspectionTriggerStatusKind kind)
    {
        return kind switch
        {
            InspectionTriggerStatusKind.Disabled => "PLC\u89e6\u53d1\u672a\u542f\u7528",
            InspectionTriggerStatusKind.Waiting => "\u7b49\u5f85\u89e6\u53d1\u4fe1\u53f7",
            InspectionTriggerStatusKind.Received => "\u6536\u5230\u89e6\u53d1\u4fe1\u53f7",
            InspectionTriggerStatusKind.Processing => "\u5904\u7406\u4e2d",
            InspectionTriggerStatusKind.CompletedOk => "\u5df2\u5b8c\u6210 OK",
            InspectionTriggerStatusKind.CompletedNg => "\u5df2\u5b8c\u6210 NG",
            InspectionTriggerStatusKind.CompletedWarning => "\u5df2\u5b8c\u6210 WARNING",
            InspectionTriggerStatusKind.Error => "\u89e6\u53d1\u5f02\u5e38",
            _ => "PLC\u89e6\u53d1\u672a\u542f\u7528"
        };
    }

    private static Brush GetDecisionBackground(InspectionCycleDecision decision)
    {
        return decision switch
        {
            InspectionCycleDecision.Ok => OkBackground,
            InspectionCycleDecision.Ng => NgBackground,
            InspectionCycleDecision.Warning => WarningBackground,
            _ => NeutralBackground
        };
    }

    private static Brush GetTriggerStatusBackground(InspectionTriggerStatusKind kind)
    {
        return kind switch
        {
            InspectionTriggerStatusKind.Received or InspectionTriggerStatusKind.Processing => ActiveBackground,
            InspectionTriggerStatusKind.CompletedOk => OkBackground,
            InspectionTriggerStatusKind.CompletedNg or InspectionTriggerStatusKind.Error => NgBackground,
            InspectionTriggerStatusKind.CompletedWarning => WarningBackground,
            _ => NeutralBackground
        };
    }

    private static Brush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private sealed record CameraInspectionSummarySnapshot(
        InspectionCycleDecision Decision,
        string Summary);
}
