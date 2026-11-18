using Gatehouse.Application.LiveBuilding.ConnectorHealth;
using Gatehouse.Application.Tenancy;

namespace Gatehouse.Workers;

/// <summary>Every 30 s, per active tenant: marks stale connectors offline (<see cref="SweepOfflineConnectorsHandler"/>).</summary>
public sealed partial class ConnectorOfflineSweeper(
    IServiceScopeFactory rootScopes,
    ITenantScopeFactory tenantScopes,
    ITenantLoop tenantLoop,
    TimeProvider time,
    ILogger<ConnectorOfflineSweeper> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await SweepAsync(stoppingToken);
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

    private async Task SweepAsync(CancellationToken ct)
    {
        IReadOnlyList<TenantSummary> tenants;
        await using (var scope = rootScopes.CreateAsyncScope())
        {
            tenants = await scope.ServiceProvider.GetRequiredService<ICatalogReader>().GetActiveTenantsAsync(ct);
        }

        await tenantLoop.ForEachAsync(tenants.Select(t => t.Id), async (tenantId, token) =>
        {
            try
            {
                await using var scope = await tenantScopes.CreateScopeAsync(tenantId, token);
                var offline = await scope.ServiceProvider.GetRequiredService<SweepOfflineConnectorsHandler>()
                    .HandleAsync(new SweepOfflineConnectorsCommand(time.GetUtcNow()), token);
                if (offline > 0)
                {
                    LogOffline(offline, tenantId.Value);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // host boundary: one failing tenant must not stop the others
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogTenantFailed(ex, tenantId.Value);
            }
        }, ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Count} connectors went offline in tenant {TenantKey}")]
    private partial void LogOffline(int count, string tenantKey);

    [LoggerMessage(Level = LogLevel.Error, Message = "Connector offline sweep failed for tenant {TenantKey}")]
    private partial void LogTenantFailed(Exception ex, string tenantKey);

    [LoggerMessage(Level = LogLevel.Error, Message = "Connector offline sweep failed")]
    private partial void LogFailed(Exception ex);
}
