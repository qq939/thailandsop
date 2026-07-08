using System;
using System.Collections.Generic;
using System.Linq;

namespace VideoInferenceDemo;

public sealed record EnvironmentDiagnosticsStatusInfo(
    EnvironmentDiagnosticsState State,
    string Title,
    string Detail)
{
    public static EnvironmentDiagnosticsStatusInfo FromResult(EnvironmentDiagnosticsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var detail = BuildDetail(result);
        return result.State switch
        {
            EnvironmentDiagnosticsState.Success => new EnvironmentDiagnosticsStatusInfo(
                result.State,
                "环境正常",
                detail),
            EnvironmentDiagnosticsState.Warning => new EnvironmentDiagnosticsStatusInfo(
                result.State,
                "环境警告",
                detail),
            _ => new EnvironmentDiagnosticsStatusInfo(
                result.State,
                "环境异常",
                detail)
        };
    }

    private static string BuildDetail(EnvironmentDiagnosticsResult result)
    {
        var lines = new List<string> { result.Summary };
        var checks = result.Report?.Checks ?? Array.Empty<DiagnosticsCheckResult>();
        if (checks.Count == 0)
        {
            return string.Join(Environment.NewLine, lines);
        }

        var requestedProviders = checks
            .FirstOrDefault(check =>
                check.Category == "ORT Config" &&
                check.Name == "Requested providers")
            ?.Message;
        var selectedProvider = checks
            .FirstOrDefault(check =>
                check.Category == "ORT" &&
                check.Name == "Selected provider")
            ?.Message;
        var errors = checks.Count(check => check.Severity == DiagnosticsSeverity.Error);
        var warnings = checks.Count(check => check.Severity == DiagnosticsSeverity.Warning);
        var missingFiles = checks.Count(check =>
            check.Category == "Files" &&
            check.Message.StartsWith("Missing:", StringComparison.OrdinalIgnoreCase));
        var loadLibraryFailures = checks.Count(check =>
            check.Category == "LoadLibrary" &&
            check.Severity != DiagnosticsSeverity.Info &&
            check.Message.StartsWith("LoadLibrary failed", StringComparison.OrdinalIgnoreCase));
        var providerInitLogPath = checks
            .FirstOrDefault(check =>
                check.Category == "ORT" &&
                check.Name == "Provider init log")
            ?.Message;

        if (!string.IsNullOrWhiteSpace(requestedProviders))
        {
            lines.Add($"请求 provider: {requestedProviders}");
        }

        if (!string.IsNullOrWhiteSpace(selectedProvider))
        {
            lines.Add($"实际 provider: {selectedProvider}");
        }

        lines.Add($"检查统计: 错误 {errors}，警告 {warnings}，缺失文件 {missingFiles}，LoadLibrary 失败 {loadLibraryFailures}");

        if (!string.IsNullOrWhiteSpace(providerInitLogPath))
        {
            lines.Add($"provider 日志: {providerInitLogPath}");
        }

        var recommendation = ResolveRecommendation(checks);
        if (!string.IsNullOrWhiteSpace(recommendation))
        {
            lines.Add($"建议: {recommendation}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string? ResolveRecommendation(IReadOnlyList<DiagnosticsCheckResult> checks)
    {
        var tensorRtRefresh = checks.FirstOrDefault(check =>
            check.Category == "Environment" &&
            check.Name == "TensorRT process refresh");
        if (tensorRtRefresh != null)
        {
            return "当前进程没有刷新到最新 TensorRT 环境变量，重启 IDE 或应用后再试。";
        }

        var loadLibrary126 = checks.FirstOrDefault(check =>
            check.Category == "LoadLibrary" &&
            check.Message.Contains("(126)", StringComparison.OrdinalIgnoreCase));
        if (loadLibrary126 != null)
        {
            return $"DLL 已找到但依赖链不可见，优先检查 PATH、CUDA/cuDNN/TensorRT 目录是否进入当前进程。可查看 provider 初始化日志获取详细失败原因。";
        }

        var fallback = checks.FirstOrDefault(check =>
            check.Category == "ORT" &&
            check.Name == "Provider fallback");
        if (fallback != null)
        {
            return $"发生 provider 回退，先看日志里的 LoadLibrary 和 Files 段，再查看 provider 初始化日志。";
        }

        var anyProviderFailure = checks.FirstOrDefault(check =>
            check.Category == "ORT" &&
            check.Name != "Session Probe" &&
            check.Name != "Selected provider" &&
            check.Name != "Provider init log" &&
            check.Severity == DiagnosticsSeverity.Error);
        if (anyProviderFailure != null)
        {
            return $"Provider 初始化失败 ({anyProviderFailure.Name})。查看 provider 初始化日志获取完整异常堆栈。";
        }

        var firstIssue = checks.FirstOrDefault(check => check.Severity != DiagnosticsSeverity.Info);
        return firstIssue?.Message;
    }
}
