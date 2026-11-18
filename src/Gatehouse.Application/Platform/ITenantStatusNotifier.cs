using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Application.Platform;

/// <summary>
/// Tells every other process (Api, Workers, Gateway) that a tenant's status changed so they drop their cached tenant resolution
/// (GH-AUTH-03). Called only after the catalog change is committed; a failed publish never fails the request (the 10 s cache
/// duration of the tenant connection stays the bound).
/// </summary>
public interface ITenantStatusNotifier
{
    public Task PublishAsync(TenantId tenantId, TenantStatus status, CancellationToken ct);
}
