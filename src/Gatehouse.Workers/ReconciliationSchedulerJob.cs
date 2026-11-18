using Gatehouse.Application.Reconciliation.Scheduling;

namespace Gatehouse.Workers;

/// <summary>Runs <see cref="ReconciliationScheduleRunner"/> one minute after start and then hourly.</summary>
public sealed partial class ReconciliationSchedulerJob(
    ReconciliationScheduleRunner runner,
    TimeProvider time,
    ILogger<ReconciliationSchedulerJob> logger) : BackgroundService
{
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstRunDelay, time, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval, time);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Reconciliation scheduler run failed")]
    private partial void LogFailed(Exception ex);
}
