using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Gatehouse.Infrastructure.Tenancy;

public sealed class TenantConnectionResolver(
    CatalogDbContext catalog,
    ISecretProtector protector,
    IMemoryCache cache) : ITenantConnectionResolver
{
    /// <summary>
    /// Fallback bound for other processes: a status change is pushed to them through <see cref="TenantStatusSubscriber"/> (GH-AUTH-03,
    /// ≤ 2 s); when Redis is unavailable another API/gateway process keeps serving a suspended tenant for at most this long.
    /// </summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);

    public async Task<Result<string>> ResolveAsync(TenantId tenantId, CancellationToken ct)
    {
        var key = CacheKey(tenantId);
        if (cache.TryGetValue(key, out string? cached) && cached is not null)
        {
            return cached;
        }

        var tenant = await catalog.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Status, t.EncryptedConnectionString })
            .SingleOrDefaultAsync(ct);

        if (tenant is null)
        {
            return Error.Forbidden("tenant.unknown", "The tenant is not known.");
        }

        if (tenant.Status != TenantStatus.Active)
        {
            return Error.Forbidden("tenant.inactive", "The tenant is not active.");
        }

        var connectionString = protector.Unprotect(tenant.EncryptedConnectionString);
        cache.Set(key, connectionString, CacheDuration);
        return connectionString;
    }

    public static string CacheKey(TenantId tenantId) => $"tenant-cs:{tenantId.Value}";
}
