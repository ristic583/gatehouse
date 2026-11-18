using Gatehouse.Application.Platform.Provisioning;
using Microsoft.Extensions.Options;

namespace Gatehouse.Workers;

/// <summary>Runs <see cref="ITenantProvisioningRunner"/> every <c>Provisioning:IntervalSeconds</c> (ADR-009 §3).</summary>
public sealed partial class TenantProvisioningJob(
    ITenantProvisioningRunner runner,
    IOptions<ProvisioningOptions> options,
    TimeProvider time,
    ILogger<TenantProvisioningJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervalSeconds)), time);
        do
        {
            try
            {
                await runner.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // host boundary (worker loop): the failed run is logged and retried on the next tick
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Tenant provisioning run failed")]
    private partial void LogFailed(Exception ex);
}
