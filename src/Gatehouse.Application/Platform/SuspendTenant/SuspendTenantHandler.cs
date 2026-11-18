using Gatehouse.Application.Platform.Dtos;
using Gatehouse.Application.Platform.Tenants;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Platform.SuspendTenant;

/// <summary>Catalog <c>Suspended</c> + Keycloak organization disabled; tenant users and connectors are refused from the next
/// request.</summary>
public sealed class SuspendTenantHandler(
    IPlatformCatalogDb catalog,
    IPlatformAuditLog audit,
    IPlatformOperator platformOperator,
    IIdentityProvisioner identity,
    ITenantAccessCache accessCache,
    ITenantStatusNotifier statusNotifier,
    PlatformTenantViews views,
    TimeProvider time)
{
    public async Task<Result<PlatformTenantDto>> HandleAsync(string tenantId, SuspendTenantRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = PlatformErrors.ParseTenantId(tenantId);
        if (id.IsFailure)
        {
            return id.Error;
        }

        var tenant = await catalog.Tenants.SingleOrDefaultAsync(t => t.Id == id.Value, ct);
        if (tenant is null)
        {
            return PlatformErrors.TenantNotFound;
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        var suspended = tenant.Suspend(reason, time.GetUtcNow());
        if (suspended.IsFailure)
        {
            return suspended.Error;
        }

        var disabled = await identity.SetOrganizationEnabledAsync(tenant.Id, false, ct);
        if (disabled.IsFailure)
        {
            return disabled.Error;
        }

        await audit.WriteAsync(platformOperator.AsActor(), PlatformActions.TenantSuspended, tenant.Id, new { reason }, ct);
        await catalog.SaveChangesAsync(ct);
        accessCache.Evict(tenant.Id);
        await statusNotifier.PublishAsync(tenant.Id, tenant.Status, ct);
        return await views.GetAsync(tenant.Id, ct);
    }
}
