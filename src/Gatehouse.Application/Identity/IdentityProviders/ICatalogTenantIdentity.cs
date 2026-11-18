using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Application.Identity.IdentityProviders;

/// <summary>The catalog fields of the current tenant that identity-provider admin needs (email domain is read only).</summary>
public interface ICatalogTenantIdentity
{
    public Task<string?> GetEmailDomainAsync(TenantId tenantId, CancellationToken ct);

    public Task SetIdpKindAsync(TenantId tenantId, IdpKind kind, CancellationToken ct);
}
