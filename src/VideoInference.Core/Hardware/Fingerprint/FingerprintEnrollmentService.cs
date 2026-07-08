using System;
using System.Threading;
using System.Threading.Tasks;

namespace VideoInferenceDemo;

public sealed record FingerprintEnrollmentResult(
    bool Success,
    byte FingerprintId,
    int? TemplateCount,
    string? FailureReason = null)
{
    public string DisplayText => Success
        ? $"指纹 {FingerprintId} 录入成功（模板数：{TemplateCount}）"
        : $"录入失败：{FailureReason}";
}

public sealed class FingerprintEnrollmentService
{
    private static readonly TimeSpan EnrollmentTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 查询模块模板数，自动分配下一个可用 ID 并录入。
    /// </summary>
    public async Task<FingerprintEnrollmentResult> EnrollNextAvailableAsync(
        FingerprintModuleOptions moduleOptions,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(moduleOptions);

        using var client = NModbusRegisterClient.Create(moduleOptions);
        using var module = new FingerprintModule(client, moduleOptions.SlaveAddress);

        var templateCount = await module.ReadTemplateCountAsync(ct);
        var fingerprintId = (byte)(templateCount + 1);
        if (fingerprintId == 0)
        {
            return new FingerprintEnrollmentResult(false, 0, templateCount, $"指纹模块已满（{templateCount} 个模板）");
        }

        return await EnrollCoreAsync(module, fingerprintId, templateCount, ct);
    }

    /// <summary>
    /// 使用指定 ID 录入指纹。
    /// </summary>
    public async Task<FingerprintEnrollmentResult> EnrollAsync(
        byte fingerprintId,
        FingerprintModuleOptions moduleOptions,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(moduleOptions);

        using var client = NModbusRegisterClient.Create(moduleOptions);
        using var module = new FingerprintModule(client, moduleOptions.SlaveAddress);

        return await EnrollCoreAsync(module, fingerprintId, null, ct);
    }

    private static async Task<FingerprintEnrollmentResult> EnrollCoreAsync(
        FingerprintModule module,
        byte fingerprintId,
        int? preReadTemplateCount,
        CancellationToken ct)
    {
        await module.SetLightAsync(FingerprintLightMode.Breathing, FingerprintLightColor.Blue, ct);

        try
        {
            await module.StartEnrollmentAsync(fingerprintId, ct);

            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < EnrollmentTimeout)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(500, ct);

                try
                {
                    var status = await module.ReadStatusAsync(ct);
                    if (status != FingerprintModuleStatus.Enrollment)
                    {
                        break;
                    }
                }
                catch (InvalidOperationException)
                {
                    break;
                }
            }

            var templateCount = await module.ReadTemplateCountAsync(ct);
            return new FingerprintEnrollmentResult(true, fingerprintId, templateCount);
        }
        catch (OperationCanceledException)
        {
            await CancelEnrollmentSafe(module);
            return new FingerprintEnrollmentResult(false, fingerprintId, preReadTemplateCount, "录入已取消");
        }
        catch (Exception ex)
        {
            return new FingerprintEnrollmentResult(false, fingerprintId, preReadTemplateCount, ex.Message);
        }
        finally
        {
            await TurnOffLightSafe(module);
        }
    }

    private static async Task CancelEnrollmentSafe(FingerprintModule module)
    {
        try { await module.CancelAsync(CancellationToken.None); }
        catch (Exception ex) { CameraDiagnostics.Error("fingerprint", "Failed to cancel enrollment.", ex); }
    }

    private static async Task TurnOffLightSafe(FingerprintModule module)
    {
        try { await module.SetLightAsync(FingerprintLightMode.AlwaysOff, FingerprintLightColor.Blue, CancellationToken.None); }
        catch (Exception ex) { CameraDiagnostics.Error("fingerprint", "Failed to turn off fingerprint light.", ex); }
    }
}
