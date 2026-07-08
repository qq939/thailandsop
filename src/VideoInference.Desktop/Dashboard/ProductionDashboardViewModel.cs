using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VideoInferenceDemo;

public sealed record EmployeeFilterItem(string EmployeeCode, string DisplayText)
{
    public static EmployeeFilterItem All { get; } = new(string.Empty, "全部员工");
}

public sealed record TeamFilterItem(string Team, string DisplayText)
{
    public static TeamFilterItem All { get; } = new(string.Empty, "全部班组");
}

public sealed record OperatorProductionKpiItem(
    string EmployeeCode,
    string EmployeeName,
    long RunCount,
    long TotalWorkDurationMs,
    double AvgWorkDurationMs,
    long OkCount,
    long NgCount,
    double YieldPercent,
    long FirstAssignedUtcMs,
    long LastEndedUtcMs)
{
    public string TotalWorkDurationText => FormatDuration(TotalWorkDurationMs);
    public string AvgWorkDurationText => FormatDuration((long)Math.Round(AvgWorkDurationMs));
    public string YieldText => $"{YieldPercent:0.00}%";

    private static string FormatDuration(long ms)
    {
        if (ms <= 0)
        {
            return "-";
        }

        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes:00}:{ts.Seconds:00}";
    }
}

public sealed partial class ProductionDashboardViewModel : ObservableObject
{
    private readonly ProductionDashboardQueryService _queryService;
    private readonly PersonnelRepository _personnelRepository;
    private readonly Func<string, string, string?> _saveCsvFilePath;

    public ProductionDashboardViewModel(
        ProductionDashboardQueryService queryService,
        PersonnelRepository personnelRepository,
        Func<string, string, string?> saveCsvFilePath)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _personnelRepository = personnelRepository ?? throw new ArgumentNullException(nameof(personnelRepository));
        _saveCsvFilePath = saveCsvFilePath ?? throw new ArgumentNullException(nameof(saveCsvFilePath));

        var today = DateTime.Today;
        fromDate = today.AddDays(-7);
        toDate = today.AddDays(1);
        statusText = "加载中...";

        _ = Refresh();
    }

    public ObservableCollection<EmployeeFilterItem> EmployeeFilters { get; } = new();
    public ObservableCollection<TeamFilterItem> TeamFilters { get; } = new();
    public ObservableCollection<OperatorProductionKpiItem> Rows { get; } = new();

    [ObservableProperty]
    private DateTime fromDate;

    [ObservableProperty]
    private DateTime toDate;

    [ObservableProperty]
    private EmployeeFilterItem selectedEmployee = EmployeeFilterItem.All;

    [ObservableProperty]
    private TeamFilterItem selectedTeam = TeamFilterItem.All;

    [ObservableProperty]
    private string statusText = "就绪";

    [ObservableProperty]
    private long totalRunCount;

    [ObservableProperty]
    private long totalOkCount;

    [ObservableProperty]
    private long totalNgCount;

    [ObservableProperty]
    private string totalWorkDurationText = "-";

    [ObservableProperty]
    private string overallYieldText = "-";

    [ObservableProperty]
    private bool isRefreshing;

    [RelayCommand]
    private async Task Refresh()
    {
        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        StatusText = "刷新中...";
        try
        {
            var from = new DateTimeOffset(FromDate.Date, TimeSpan.Zero);
            var to = new DateTimeOffset(ToDate.Date, TimeSpan.Zero);
            if (to <= from)
            {
                to = from.AddDays(1);
                ToDate = to.DateTime;
            }

            var selectedEmployeeCode = SelectedEmployee?.EmployeeCode ?? string.Empty;
            var selectedTeamCode = SelectedTeam?.Team ?? string.Empty;

            var snapshot = await Task.Run(() => QuerySnapshot(
                from,
                to,
                selectedEmployeeCode,
                selectedTeamCode));

            ApplySnapshot(snapshot);
        }
        catch (Exception ex)
        {
            StatusText = $"刷新失败: {ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private void ExportCsv()
    {
        if (Rows.Count == 0)
        {
            StatusText = "当前没有可导出的统计数据";
            return;
        }

        var defaultName = $"生产统计_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        var savePath = _saveCsvFilePath(defaultName, "导出生产统计 CSV");
        if (string.IsNullOrWhiteSpace(savePath))
        {
            StatusText = "已取消导出";
            return;
        }

        var csv = BuildCsv();
        File.WriteAllText(savePath, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        StatusText = $"已导出 {Rows.Count} 条统计到 {savePath}";
    }

    private DashboardSnapshot QuerySnapshot(
        DateTimeOffset from,
        DateTimeOffset to,
        string selectedEmployeeCode,
        string selectedTeamCode)
    {
        var employeeFilters = BuildEmployeeFilters();
        var teamFilters = BuildTeamFilters();

        var finalEmployee = employeeFilters.Any(item => string.Equals(item.EmployeeCode, selectedEmployeeCode, StringComparison.OrdinalIgnoreCase))
            ? selectedEmployeeCode
            : string.Empty;
        var finalTeam = teamFilters.Any(item => string.Equals(item.Team, selectedTeamCode, StringComparison.OrdinalIgnoreCase))
            ? selectedTeamCode
            : string.Empty;

        var rows = _queryService.QueryOperatorKpis(
            from,
            to,
            finalEmployee,
            finalTeam);

        var rowItems = rows.Select(row => new OperatorProductionKpiItem(
            row.EmployeeCode,
            row.EmployeeName,
            row.RunCount,
            row.TotalWorkDurationMs,
            row.AvgWorkDurationMs,
            row.OkCount,
            row.NgCount,
            row.YieldPercent,
            row.FirstAssignedUtcMs,
            row.LastEndedUtcMs)).ToList();

        var totalRunCount = rowItems.Sum(item => item.RunCount);
        var totalOkCount = rowItems.Sum(item => item.OkCount);
        var totalNgCount = rowItems.Sum(item => item.NgCount);
        var totalMs = rowItems.Sum(item => item.TotalWorkDurationMs);
        var total = totalOkCount + totalNgCount;
        var yieldText = total > 0 ? $"{totalOkCount * 100d / total:0.00}%" : "-";

        return new DashboardSnapshot(
            employeeFilters,
            teamFilters,
            finalEmployee,
            finalTeam,
            rowItems,
            totalRunCount,
            totalOkCount,
            totalNgCount,
            FormatDuration(totalMs),
            yieldText,
            $"已加载 {rowItems.Count} 条人员统计");
    }

    private List<EmployeeFilterItem> BuildEmployeeFilters()
    {
        var list = new List<EmployeeFilterItem> { EmployeeFilterItem.All };
        foreach (var item in _personnelRepository.List(includeInactive: false))
        {
            list.Add(new EmployeeFilterItem(item.EmployeeCode, $"{item.EmployeeName} ({item.EmployeeCode})"));
        }

        return list;
    }

    private List<TeamFilterItem> BuildTeamFilters()
    {
        var list = new List<TeamFilterItem> { TeamFilterItem.All };
        foreach (var team in _personnelRepository.ListTeams(includeInactive: false))
        {
            list.Add(new TeamFilterItem(team, team));
        }

        return list;
    }

    private void ApplySnapshot(DashboardSnapshot snapshot)
    {
        EmployeeFilters.Clear();
        foreach (var filter in snapshot.EmployeeFilters)
        {
            EmployeeFilters.Add(filter);
        }

        TeamFilters.Clear();
        foreach (var filter in snapshot.TeamFilters)
        {
            TeamFilters.Add(filter);
        }

        SelectedEmployee = EmployeeFilters.FirstOrDefault(item =>
            string.Equals(item.EmployeeCode, snapshot.SelectedEmployeeCode, StringComparison.OrdinalIgnoreCase)) ?? EmployeeFilterItem.All;
        SelectedTeam = TeamFilters.FirstOrDefault(item =>
            string.Equals(item.Team, snapshot.SelectedTeam, StringComparison.OrdinalIgnoreCase)) ?? TeamFilterItem.All;

        Rows.Clear();
        foreach (var row in snapshot.Rows)
        {
            Rows.Add(row);
        }

        TotalRunCount = snapshot.TotalRunCount;
        TotalOkCount = snapshot.TotalOkCount;
        TotalNgCount = snapshot.TotalNgCount;
        TotalWorkDurationText = snapshot.TotalWorkDurationText;
        OverallYieldText = snapshot.OverallYieldText;
        StatusText = snapshot.StatusText;
    }

    private string BuildCsv()
    {
        var builder = new StringBuilder();
        builder.AppendLine("工号,姓名,作业次数,总作业时长,平均作业时长,OK数量,NG数量,良率");
        foreach (var row in Rows)
        {
            builder.AppendLine(string.Join(",",
                EscapeCsv(row.EmployeeCode),
                EscapeCsv(row.EmployeeName),
                row.RunCount.ToString(),
                EscapeCsv(row.TotalWorkDurationText),
                EscapeCsv(row.AvgWorkDurationText),
                row.OkCount.ToString(),
                row.NgCount.ToString(),
                EscapeCsv(row.YieldText)));
        }

        return builder.ToString();
    }

    private static string EscapeCsv(string value)
    {
        var text = value ?? string.Empty;
        if (!text.Contains(',') && !text.Contains('"') && !text.Contains('\n') && !text.Contains('\r'))
        {
            return text;
        }

        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string FormatDuration(long ms)
    {
        if (ms <= 0)
        {
            return "-";
        }

        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes:00}:{ts.Seconds:00}";
    }

    private sealed record DashboardSnapshot(
        IReadOnlyList<EmployeeFilterItem> EmployeeFilters,
        IReadOnlyList<TeamFilterItem> TeamFilters,
        string SelectedEmployeeCode,
        string SelectedTeam,
        IReadOnlyList<OperatorProductionKpiItem> Rows,
        long TotalRunCount,
        long TotalOkCount,
        long TotalNgCount,
        string TotalWorkDurationText,
        string OverallYieldText,
        string StatusText);
}
