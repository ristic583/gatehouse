using Gatehouse.Application.Platform.Provisioning;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Platform.ResendAdminInvite;

/// <summary>Resends the first-admin email, only while that admin never signed in.</summary>
public sealed class ResendAdminInviteHandler(
    IPlatformCatalogDb catalog,
    IPlatformAuditLog audit,
    IPlatformOperator platformOperator,
    IIdentityProvisioner identity)
{
    public async Task<Result> HandleAsync(string tenantId, CancellationToken ct)
    {
        var id = PlatformErrors.ParseTenantId(tenantId);
        if (id.IsFailure)
        {
            return id.Error;
        }

        var tenantKey = id.Value;
        if (!await catalog.Tenants.AnyAsync(t => t.Id == tenantKey, ct))
        {
            return PlatformErrors.TenantNotFound;
        }

        var provisioning = await catalog.TenantProvisionings.SingleOrDefaultAsync(p => p.TenantId == tenantKey, ct);
        if (provisioning is null || provisioning.AdminActivatedAt is not null)
        {
            return Error.Conflict("tenant.admin_already_active", "The first tenant admin has already signed in.");
        }

        if (provisioning.AdminInvitedAt is null)
        {
            return Error.Conflict("tenant.invalid_status_transition", "The admin has not been invited yet; provisioning is still running.");
        }

        var invited = await identity.InviteTenantAdminAsync(
            tenantKey, provisioning.AdminEmail, provisioning.AdminGivenName, provisioning.AdminFamilyName, ct);
        if (invited.IsFailure)
        {
            return invited.Error;
        }

        await audit.WriteAsync(platformOperator.AsActor(), ProvisioningActions.AdminInviteSent, tenantKey, null, ct);
        await catalog.SaveChangesAsync(ct);
        return Result.Success();
    }
}
