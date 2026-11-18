using Gatehouse.Application.Platform.Dtos;
using Gatehouse.Application.Platform.Tenants;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Platform.ReactivateTenant;

public sealed class ReactivateTenantHandler(
    IPlatformCatalogDb catalog,
    IPlatformAuditLog audit,
    IPlatformOperator platformOperator,
    IIdentityProvisioner identity,
    ITenantAccessCache accessCache,
    ITenantStatusNotifier statusNotifier,
    PlatformTenantViews views)
{
    public async Task<Result<PlatformTenantDto>> HandleAsync(string tenantId, CancellationToken ct)
    {
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

        var reactivated = tenant.Reactivate();
        if (reactivated.IsFailure)
        {
            return reactivated.Error;
        }

        var enabled = await identity.SetOrganizationEnabledAsync(tenant.Id, true, ct);
        if (enabled.IsFailure)
        {
            return enabled.Error;
        }

        await audit.WriteAsync(platformOperator.AsActor(), PlatformActions.TenantReactivated, tenant.Id, null, ct);
        await catalog.SaveChangesAsync(ct);
        accessCache.Evict(tenant.Id);
        await statusNotifier.PublishAsync(tenant.Id, tenant.Status, ct);
        return await views.GetAsync(tenant.Id, ct);
    }
}
