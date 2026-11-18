using Gatehouse.Application.Tenancy;
using Gatehouse.Infrastructure.Messaging;

namespace Gatehouse.Workers;

public sealed partial class OutboxRelayWorker(
    IServiceScopeFactory rootScopes,
    OutboxRelay relay,
    OutboxRelayScheduler scheduler,
    ITenantLoop tenantLoop,
    IConfiguration configuration,
    ILogger<OutboxRelayWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(configuration.GetValue("Outbox:RelayIntervalMs", 250)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                IReadOnlyList<TenantSummary> tenants;
                await using (var scope = rootScopes.CreateAsyncScope())
                {
                    tenants = await scope.ServiceProvider.GetRequiredService<ICatalogReader>().GetActiveTenantsAsync(stoppingToken);
                }

                // Only signalled or due tenants, plus a periodic sweep: idle tenant databases are not reconnected every tick (M11).
                var visit = await scheduler.SelectAsync(tenants.Select(t => t.Id).ToList(), stoppingToken);
                await tenantLoop.ForEachAsync(visit, async (tenantId, token) =>
                {
                    try
                    {
                        scheduler.Record(tenantId, await relay.RelayAsync(tenantId, token));
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        return;
                    }
#pragma warning disable CA1031 // host boundary: one failing tenant must not stop the loop
                    catch (Exception ex)
#pragma warning restore CA1031
                    {
                        scheduler.RecordFailure(tenantId);
                        LogTenantFailed(ex, tenantId.Value);
                    }
                }, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // host boundary (worker loop): the failed run is logged and retried on the next tick
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogLoopFailed(ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox relay failed for tenant {TenantId}")]
    private partial void LogTenantFailed(Exception ex, string tenantId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox relay loop iteration failed")]
    private partial void LogLoopFailed(Exception ex);
}
