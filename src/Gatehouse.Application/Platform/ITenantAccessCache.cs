using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Platform;

/// <summary>Drops cached tenant resolution so a status change (suspend/reactivate) applies to the next request.</summary>
public interface ITenantAccessCache
{
    public void Evict(TenantId tenantId);
}
