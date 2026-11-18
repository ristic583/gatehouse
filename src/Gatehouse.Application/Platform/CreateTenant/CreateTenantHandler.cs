using Gatehouse.Application.Platform.Dtos;
using Gatehouse.Application.Platform.Tenants;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Platform;
using Gatehouse.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Platform.CreateTenant;

/// <summary>Writes the catalog rows that the provisioning runner picks up (ADR-009 §3); the API answers 202.</summary>
public sealed class CreateTenantHandler(
    IPlatformCatalogDb catalog,
    IPlatformAuditLog audit,
    IPlatformOperator platformOperator,
    PlatformTenantViews views,
    TimeProvider time)
{
    public async Task<Result<PlatformTenantDto>> HandleAsync(CreateTenantRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = TenantId.Create(request.TenantId);
        if (id.IsFailure)
        {
            return id.Error;
        }

        var tenantId = id.Value;
        if (await catalog.Tenants.AnyAsync(t => t.Id == tenantId, ct))
        {
            return Error.Conflict("tenant.already_exists", $"Tenant '{tenantId}' already exists.");
        }

        var domain = request.EmailDomain.Trim().ToLowerInvariant();
        if (await catalog.Tenants.AnyAsync(t => t.EmailDomain == domain, ct))
        {
            return Error.Conflict("tenant.email_domain_taken", "Another tenant already uses this email domain.");
        }

        var now = time.GetUtcNow();
        var tenant = Tenant.CreateProvisioning(tenantId, request.DisplayName.Trim(), domain, now);
        if (tenant.IsFailure)
        {
            return tenant.Error;
        }

        catalog.Tenants.Add(tenant.Value);
        catalog.TenantProvisionings.Add(TenantProvisioning.Start(
            tenantId, request.AdminEmail.Trim()
                .ToLowerInvariant(), request.AdminGivenName
                .Trim(), request.AdminFamilyName
                .Trim(), request.TimeZoneId, now));
        await audit.WriteAsync(platformOperator.AsActor(), PlatformActions.TenantCreated, tenantId,
            new { displayName = tenant.Value.DisplayName, emailDomain = domain }, ct);
        await catalog.SaveChangesAsync(ct);

        return await views.GetAsync(tenantId, ct);
    }
}
