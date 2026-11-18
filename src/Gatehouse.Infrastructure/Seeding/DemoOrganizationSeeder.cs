using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Identity.Keycloak;
using Microsoft.Extensions.Options;

namespace Gatehouse.Infrastructure.Seeding;

/// <summary>
/// Links a demo tenant to its Keycloak organization the way platform provisioning does (alias = name = tenant id, verified
/// domain) and makes the tenant's existing identity users members, so suspension disables their sign-in. Idempotent: a
/// realm-imported organization with the tenant's alias is adopted, never recreated.
/// </summary>
public sealed class DemoOrganizationSeeder(IIdentityProvisioner identity, IOptions<KeycloakOptions> options)
{
    /// <summary>False when no provisioner secret is configured (CLI runs without Keycloak); the step is then skipped.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.ProvisionerClientSecret);

    public async Task<Result> EnsureAsync(TenantId tenantId, string displayName, string emailDomain, CancellationToken ct)
    {
        var organization = await identity.EnsureOrganizationAsync(tenantId, displayName, emailDomain, ct);
        return organization.IsFailure ? organization : await identity.EnsureOrganizationMembersAsync(tenantId, ct);
    }
}
