using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Application.Identity.IdentityProviders;

/// <summary>Builds <see cref="IdentityProviderDto"/> from the stored summary, the catalog email domain and the broker endpoints.</summary>
public sealed class IdentityProviderViews(ITenantContext tenant, ICatalogTenantIdentity catalog, IIdentityProvisioner identity)
{
    public async Task<Result<IdentityProviderDto?>> ToDtoAsync(TenantIdentityProvider? summary, CancellationToken ct)
    {
        if (summary is null)
        {
            return (IdentityProviderDto?)null;
        }

        var provider = await identity.GetProviderAsync(tenant.TenantId, ct);
        if (provider.IsFailure)
        {
            return provider.Error;
        }

        var domain = await catalog.GetEmailDomainAsync(tenant.TenantId, ct) ?? string.Empty;
        return new IdentityProviderDto(
            summary.Kind, summary.Alias, summary.DisplayName, summary.DiscoveryUrl, summary.Issuer, summary.ClientId,
            HasClientSecret: summary.Kind == IdpKind.Oidc,
            summary.SamlEntityId, summary.SamlSsoUrl, summary.SigningCertificateThumbprint, domain,
            provider.Value?.BrokerRedirectUri ?? string.Empty, provider.Value?.SpEntityId,
            summary.SavedAt, summary.LastTestedAt, summary.LastTestOk);
    }
}
