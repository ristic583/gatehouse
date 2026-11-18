using Gatehouse.Application.Tenancy.IdentityProvisioning;
using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Tenancy;

/// <summary>
/// Keycloak admin operations for tenant onboarding (ADR-009 §4). Organization alias, IdP alias and the
/// <c>tenant_id</c> mapper value are always derived from the <see cref="TenantId"/> argument, never from request input.
/// Failures of the identity server are <c>idp.provisioning_failed</c> and never echo secrets.
/// </summary>
public interface IIdentityProvisioner
{
    /// <summary>Creates or updates the organization (alias = tenant id, verified domain, redirect on email match).
    /// <c>tenant.email_domain_taken</c> when another organization owns the domain.</summary>
    public Task<Result> EnsureOrganizationAsync(TenantId tenantId, string displayName, string emailDomain, CancellationToken ct);

    public Task<Result> SetOrganizationEnabledAsync(TenantId tenantId, bool enabled, CancellationToken ct);

    /// <summary>Adds every identity user whose <c>tenant_id</c> is the tenant to its organization (demo seed: users created
    /// before the organization was linked). <c>tenant.not_found</c> without an organization.</summary>
    public Task<Result> EnsureOrganizationMembersAsync(TenantId tenantId, CancellationToken ct);

    /// <summary>Local user with <c>tenant_id</c>, organization member, <c>UPDATE_PASSWORD</c> email valid 72 h. Existing user ⇒
    /// resend.</summary>
    public Task<Result> InviteTenantAdminAsync(TenantId tenantId, string email, string givenName, string familyName, CancellationToken ct);

    /// <summary>True once the invited user completed sign-in (password set or a brokered identity linked).</summary>
    public Task<Result<bool>> HasSignedInAsync(TenantId tenantId, string email, CancellationToken ct);

    public Task<Result<ProvisionedProvider>> UpsertOidcProviderAsync(TenantId tenantId, OidcProviderSpec spec, CancellationToken ct);

    public Task<Result<ProvisionedProvider>> UpsertSamlProviderAsync(TenantId tenantId, SamlProviderSpec spec, CancellationToken ct);

    /// <summary><c>idp.not_configured</c> when the tenant has no provider.</summary>
    public Task<Result> RemoveProviderAsync(TenantId tenantId, CancellationToken ct);

    public Task<Result<ProvisionedProvider?>> GetProviderAsync(TenantId tenantId, CancellationToken ct);
}
