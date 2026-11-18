using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Tenancy;

/// <summary>
/// Runs one body per tenant for a background loop with bounded parallelism (<c>Tenancy:MaxConcurrentTenants</c>, default 4).
/// Every tenant database has its own connection pool, so a loop over all tenants must not hold connections to all of them:
/// when a tenant's body ends, its idle connections are closed before the next tenant takes the slot. A loop therefore holds
/// at most that many tenant connections at once, however many tenants exist (connection exhaustion, M11).
/// </summary>
public interface ITenantLoop
{
    /// <summary>Runs <paramref name="body"/> for each tenant; bodies catch their own per-tenant failures (host boundary).</summary>
    public Task ForEachAsync(IEnumerable<TenantId> tenants, Func<TenantId, CancellationToken, Task> body, CancellationToken ct);
}
