using Gatehouse.Application.Platform.Dtos;
using Gatehouse.Application.Platform.Tenants;
using Gatehouse.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Platform.RetryProvisioning;

/// <summary>Resumes provisioning at the failed step (the Workers runner picks it up).</summary>
public sealed class RetryProvisioningHandler(
    IPlatformCatalogDb catalog,
    IPlatformAuditLog audit,
    IPlatformOperator platformOperator,
    PlatformTenantViews views,
    TimeProvider time)
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

        var provisioning = await catalog.TenantProvisionings.SingleOrDefaultAsync(p => p.TenantId == tenant.Id, ct);
        if (provisioning is null)
        {
            return Error.Conflict("tenant.provisioning_not_failed", "Provisioning has not failed.");
        }

        var retried = provisioning.Retry(time.GetUtcNow());
        if (retried.IsFailure)
        {
            return retried.Error;
        }

        var status = tenant.RetryProvisioning();
        if (status.IsFailure)
        {
            return status.Error;
        }

        await audit.WriteAsync(
            platformOperator.AsActor(), PlatformActions.ProvisioningRetried, tenant.Id, new { step = provisioning.Step.ToString() }, ct);
        await catalog.SaveChangesAsync(ct);
        return await views.GetAsync(tenant.Id, ct);
    }
}
