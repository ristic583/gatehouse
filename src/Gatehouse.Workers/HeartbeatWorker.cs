namespace Gatehouse.Workers;

public sealed class HeartbeatWorker(ILogger<HeartbeatWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Workers alive");
            await Task.Delay(Interval, stoppingToken);
        }
    }
}
