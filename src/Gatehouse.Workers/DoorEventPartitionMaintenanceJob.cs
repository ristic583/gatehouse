using Gatehouse.Infrastructure.LiveBuilding;

namespace Gatehouse.Workers;

/// <summary>Runs <see cref="DoorEventPartitionMaintenance"/> at start and then hourly.</summary>
public sealed partial class DoorEventPartitionMaintenanceJob(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<DoorEventPartitionMaintenanceJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DoorEventPartitionMaintenance>().RunOnceAsync(stoppingToken);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Door event partition maintenance failed")]
    private partial void LogFailed(Exception ex);
}
