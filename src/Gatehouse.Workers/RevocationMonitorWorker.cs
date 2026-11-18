using Gatehouse.Application.Fulfillment.MonitorRevocations;
using Microsoft.Extensions.Options;

namespace Gatehouse.Workers;

public sealed partial class RevocationMonitorWorker(
    RevocationMonitorRunner runner,
    IOptions<RevocationMonitorOptions> options,
    ILogger<RevocationMonitorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval);
        do
        {
            try
            {
                var result = await runner.RunOnceAsync(stoppingToken);
                var total = result.Values.Sum();
                if (total > 0)
                {
                    LogAlerts(total);
                }
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Revocation monitor raised {Count} new alerts")]
    private partial void LogAlerts(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Revocation monitor run failed")]
    private partial void LogFailed(Exception ex);
}
