using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;

namespace Gatehouse.Infrastructure.Tenancy;

/// <inheritdoc cref="ITenantLoop"/>
public sealed class TenantLoop(int maxConcurrentTenants) : ITenantLoop
{
    public const string MaxConcurrentTenantsSetting = "Tenancy:MaxConcurrentTenants";

    public const int DefaultMaxConcurrentTenants = 4;

    public int MaxConcurrentTenants { get; } = Math.Max(1, maxConcurrentTenants);

    public Task ForEachAsync(IEnumerable<TenantId> tenants, Func<TenantId, CancellationToken, Task> body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tenants);
        ArgumentNullException.ThrowIfNull(body);

        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentTenants, CancellationToken = ct };
        return Parallel.ForEachAsync(tenants, options, async (tenant, token) =>
        {
            try
            {
                await body(tenant, token);
            }
            finally
            {
                TenantConnectionPool.Release(tenant);
            }
        });
    }
}
