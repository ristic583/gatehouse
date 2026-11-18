using Gatehouse.Application.Platform;
using Gatehouse.Infrastructure.Platform;
using Microsoft.Extensions.Options;

namespace Gatehouse.Workers;

/// <summary>Runs <see cref="TenantStatisticsCollector"/> every <c>TenantStatistics:IntervalMinutes</c> (ADR-009 §2).</summary>
public sealed partial class TenantStatisticsJob(
    TenantStatisticsCollector collector,
    IOptions<TenantStatisticsOptions> options,
    TimeProvider time,
    ILogger<TenantStatisticsJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.Value.IntervalMinutes)), time);
        do
        {
            try
            {
                await collector.RunOnceAsync(stoppingToken);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Tenant statistics run failed")]
    private partial void LogFailed(Exception ex);
}
