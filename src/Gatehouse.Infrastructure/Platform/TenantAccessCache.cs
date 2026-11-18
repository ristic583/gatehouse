using Gatehouse.Application.Platform;
using Gatehouse.Domain.Common;
using Gatehouse.Infrastructure.Tenancy;
using Microsoft.Extensions.Caching.Memory;

namespace Gatehouse.Infrastructure.Platform;

/// <summary>Evicts this process's cached tenant connection (<see cref="TenantConnectionResolver"/>); other processes expire within its
/// cache duration (10 s, the defined suspension propagation delay).</summary>
public sealed class TenantAccessCache(IMemoryCache cache) : ITenantAccessCache
{
    public void Evict(TenantId tenantId) => cache.Remove(TenantConnectionResolver.CacheKey(tenantId));
}
