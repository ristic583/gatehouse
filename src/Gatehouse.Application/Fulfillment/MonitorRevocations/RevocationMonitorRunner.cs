using System.Collections.Concurrent;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Application.Fulfillment.MonitorRevocations;

public sealed partial class RevocationMonitorRunner(
    IServiceScopeFactory rootScopes,
    ITenantScopeFactory tenantScopes,
    ITenantLoop tenantLoop,
    TimeProvider time,
    ILogger<RevocationMonitorRunner> logger)
{
    public async Task<IReadOnlyDictionary<TenantId, int>> RunOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<TenantSummary> tenants;
        await using (var scope = rootScopes.CreateAsyncScope())
        {
            tenants = await scope.ServiceProvider.GetRequiredService<ICatalogReader>().GetActiveTenantsAsync(ct);
        }

        var result = new ConcurrentDictionary<TenantId, int>();
        await tenantLoop.ForEachAsync(tenants.Select(t => t.Id), async (tenantId, token) =>
        {
            try
            {
                await using var scope = await tenantScopes.CreateScopeAsync(tenantId, token);
                var handler = scope.ServiceProvider.GetRequiredService<DetectUnconfirmedRevocationsHandler>();
                result[tenantId] = await handler.HandleAsync(new DetectUnconfirmedRevocationsCommand(time.GetUtcNow()), token);
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

        return result;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Revocation monitor failed for tenant {TenantKey}")]
    private partial void LogTenantFailed(Exception ex, string tenantKey);
}
