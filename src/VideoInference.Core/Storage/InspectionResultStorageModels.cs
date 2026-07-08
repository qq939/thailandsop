using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VideoInferenceDemo;

public enum InspectionResultMySqlSyncStatus
{
    None = 0,
    Pending = 1,
    Synced = 2,
    Failed = 3
}

public sealed record InspectionResultStorageItem(
    InspectionCycleResult Result,
    int ImageWidth,
    int ImageHeight,
    string? ImagePath,
    IReadOnlyDictionary<string, string> RoiImagePaths)
{
    public string CycleUuid { get; init; } = InspectionResultRowMapper.CreateCycleUuid(Result);
}

public sealed record InspectionResultRowSet(
    InspectionCycleRow Cycle,
    IReadOnlyList<InspectionRoiResultRow> RoiResults);

public sealed class InspectionCycleRow
{
    public string CycleUuid { get; set; } = string.Empty;
    public long TriggerUtcMs { get; set; }
    public string TriggerLocalDate { get; set; } = string.Empty;
    public string ProductModel { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string PositionNo { get; set; } = string.Empty;
    public string? StationId { get; set; }
    public string? TaskInstanceId { get; set; }
    public string? CameraId { get; set; }
    public string? ActionType { get; set; }
    public string? TriggerId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string? OperatorCode { get; set; }
    public string? OperatorName { get; set; }
    public string? SummaryMessage { get; set; }
    public string? MetadataJson { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public string? ImagePath { get; set; }
    public long CreatedUtcMs { get; set; }
    public string MySqlSyncStatus { get; set; } = InspectionResultMySqlSyncStatus.None.ToString();
    public string? MySqlSyncError { get; set; }
    public long? MySqlSyncedUtcMs { get; set; }
}

public sealed class InspectionRoiResultRow
{
    public string CycleUuid { get; set; } = string.Empty;
    public string RoiId { get; set; } = string.Empty;
    public string? RoiName { get; set; }
    public string? ModelId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public double? Score { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double AngleDeg { get; set; }
    public int SortOrder { get; set; }
    public string? FindingsJson { get; set; }
    public string? MetricsJson { get; set; }
    public int? DefectComponentCount { get; set; }
    public double? DefectMaxAreaPx { get; set; }
    public double? DefectMaxPerimeterPx { get; set; }
    public double? DefectMaxAreaPerimeterRatio { get; set; }
    public string? DefectSummaryText { get; set; }
    public string? DefectComponentsText { get; set; }
    public string? RoiImagePath { get; set; }
}

public static class InspectionResultRowMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static InspectionResultRowSet Map(
        InspectionResultStorageItem item,
        InspectionResultMySqlSyncStatus syncStatus,
        string? syncError = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        var result = item.Result;
        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var cycle = new InspectionCycleRow
        {
            CycleUuid = item.CycleUuid,
            TriggerUtcMs = result.TriggerTime.ToUniversalTime().ToUnixTimeMilliseconds(),
            TriggerLocalDate = result.TriggerTime.LocalDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ProductModel = result.RecipeKey.ProductModel,
            TaskId = result.RecipeKey.TaskId,
            PositionNo = result.RecipeKey.PositionNo,
            StationId = EmptyToNull(result.StationId),
            TaskInstanceId = EmptyToNull(result.TaskInstanceId),
            CameraId = EmptyToNull(result.CameraId),
            ActionType = EmptyToNull(result.ActionType),
            TriggerId = EmptyToNull(result.TriggerId),
            Decision = result.Decision.ToString(),
            OperatorCode = EmptyToNull(result.Operator?.EmployeeCode),
            OperatorName = EmptyToNull(result.Operator?.EmployeeName),
            SummaryMessage = EmptyToNull(result.SummaryMessage),
            MetadataJson = SerializeOrNull(result.Metadata),
            ImageWidth = Math.Max(0, item.ImageWidth),
            ImageHeight = Math.Max(0, item.ImageHeight),
            ImagePath = EmptyToNull(item.ImagePath),
            CreatedUtcMs = nowUtcMs,
            MySqlSyncStatus = syncStatus.ToString(),
            MySqlSyncError = EmptyToNull(syncError),
            MySqlSyncedUtcMs = syncStatus == InspectionResultMySqlSyncStatus.Synced ? nowUtcMs : null
        };

        var rois = result.ResolvedRois
            .OrderBy(roi => roi.SortOrder)
            .Select(roi =>
            {
                var roiResult = result.RoiResults.FirstOrDefault(candidate =>
                    string.Equals(candidate.RoiId, roi.Id, StringComparison.OrdinalIgnoreCase));
                item.RoiImagePaths.TryGetValue(roi.Id, out var roiImagePath);
                return new InspectionRoiResultRow
                {
                    CycleUuid = item.CycleUuid,
                    RoiId = roi.Id,
                    RoiName = EmptyToNull(roi.Name),
                    ModelId = EmptyToNull(roiResult?.ModelId ?? roi.ModelId),
                    Decision = (roiResult?.Decision ?? result.Decision).ToString(),
                    Score = roiResult?.Score,
                    CenterX = roi.CenterX,
                    CenterY = roi.CenterY,
                    Width = roi.Width,
                    Height = roi.Height,
                    AngleDeg = roi.AngleDeg,
                    SortOrder = roi.SortOrder,
                    FindingsJson = SerializeOrNull(roiResult?.Findings ?? Array.Empty<InspectionFinding>()),
                    MetricsJson = SerializeOrNull(roiResult?.Metrics ?? new Dictionary<string, string>()),
                    DefectComponentCount = roiResult?.DefectComponentCount,
                    DefectMaxAreaPx = roiResult?.DefectMaxAreaPx,
                    DefectMaxPerimeterPx = roiResult?.DefectMaxPerimeterPx,
                    DefectMaxAreaPerimeterRatio = roiResult?.DefectMaxAreaPerimeterRatio,
                    DefectSummaryText = EmptyToNull(roiResult?.DefectSummaryText),
                    DefectComponentsText = EmptyToNull(roiResult?.DefectComponentsText),
                    RoiImagePath = EmptyToNull(roiImagePath)
                };
            })
            .ToArray();

        return new InspectionResultRowSet(cycle, rois);
    }

    public static string CreateCycleUuid(InspectionCycleResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(result.TriggerId))
        {
            return Guid.NewGuid().ToString("N");
        }

        var stableKey = string.Join(
            "|",
            result.TriggerId,
            result.TriggerTime.ToUniversalTime().ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            result.RecipeKey.ProductModel,
            result.RecipeKey.TaskId,
            result.RecipeKey.PositionNo,
            result.TaskInstanceId ?? string.Empty,
            result.CameraId ?? string.Empty);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(stableKey));
        return Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }

    private static string? SerializeOrNull<T>(T value)
    {
        if (value == null)
        {
            return null;
        }

        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
