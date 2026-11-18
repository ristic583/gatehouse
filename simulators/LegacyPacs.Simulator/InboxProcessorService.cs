namespace LegacyPacs.Simulator;

public sealed class InboxProcessorService(InboxProcessor processor, DoorController doors, IConfiguration configuration, ILogger<InboxProcessorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMilliseconds(configuration.GetValue("Simulator:InboxIntervalMs", 2000));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await processor.ProcessOnceAsync(stoppingToken);
                await doors.RelockDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Inbox processing failed; retrying");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
