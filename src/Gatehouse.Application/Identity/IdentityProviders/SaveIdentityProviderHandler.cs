using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Tenancy;
using Gatehouse.Application.Tenancy.IdentityProvisioning;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Application.Identity.IdentityProviders;

/// <summary>
/// Creates or replaces the organization's single upstream IdP (ADR-009 §4). The Keycloak alias, organization and
/// <c>tenant_id</c> mapper come from <see cref="ITenantContext"/>, never from the request.
/// </summary>
public sealed class SaveIdentityProviderHandler(
    ITenantContext tenant,
    IdentityProviderChecks checks,
    IdentityProviderStore store,
    IdentityProviderViews views,
    IIdentityProvisioner identity,
    ICatalogTenantIdentity catalog,
    ICurrentUser user,
    IAuditLog audit,
    ITenantDb db,
    TimeProvider time)
{
    private static readonly string[] DefaultScopes = ["openid", "email", "profile"];

    public async Task<Result<IdentityProviderDto?>> HandleAsync(SaveIdentityProviderRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = tenant.TenantId;
        var existing = await store.GetAsync(ct);
        if (request.Kind == IdpKind.Oidc && string.IsNullOrEmpty(request.Oidc?.ClientSecret) && existing?.Kind != IdpKind.Oidc)
        {
            return Error.Validation("idp.invalid_config", "A client secret is required for a new OIDC provider.");
        }

        var outcome = await checks.RunAsync(request, ct);
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        if (!outcome.Value.Result.Ok)
        {
            var failed = string.Join(", ", outcome.Value.Result.Checks.Where(c => !c.Ok).Select(c => c.Name));
            return Error.Validation("idp.invalid_config", $"Identity provider checks failed: {failed}.");
        }

        // Switching kind: the provisioner removes the previous provider only after the new one exists, so a failed replacement keeps
        // the working broker (GH-ADMIN-03).
        var now = time.GetUtcNow();
        TenantIdentityProvider summary;
        Result<ProvisionedProvider> provisioned;
        if (request.Kind == IdpKind.Oidc)
        {
            var oidc = request.Oidc!;
            var d = outcome.Value.Oidc!;
            var scopes = oidc.Scopes is { Count: > 0 } s ? s : DefaultScopes;
            provisioned = await identity.UpsertOidcProviderAsync(tenantId, new OidcProviderSpec(
                request.DisplayName.Trim(), d.Issuer, d.AuthorizationUrl, d.TokenUrl, d.JwksUrl, d.UserInfoUrl, oidc.ClientId.Trim(),
                string.IsNullOrEmpty(oidc.ClientSecret) ? null : oidc.ClientSecret, scopes), ct);
            summary = new TenantIdentityProvider(IdpKind.Oidc, string.Empty, request.DisplayName.Trim(), oidc.DiscoveryUrl.Trim(), d.Issuer,
                oidc.ClientId.Trim(), null, null, null, now, now, true);
        }
        else
        {
            var m = outcome.Value.Saml!;
            provisioned = await identity.UpsertSamlProviderAsync(tenantId, new SamlProviderSpec(
                request.DisplayName.Trim(), m.EntityId, m.SsoUrl!, m.SigningCertificatePem!), ct);
            summary = new TenantIdentityProvider(IdpKind.Saml, string.Empty, request.DisplayName.Trim(), null, null, null,
                m.EntityId, m.SsoUrl, m.SigningCertificateThumbprint, now, now, true);
        }

        if (provisioned.IsFailure)
        {
            return provisioned.Error;
        }

        summary = summary with { Alias = provisioned.Value.Alias };
        await store.SetAsync(summary, now, ct);
        await catalog.SetIdpKindAsync(tenantId, request.Kind, ct);
        var actor = AuditActor.ForPerson(await user.RequirePersonIdAsync(ct));
        audit.Record(actor, "identity_provider.saved", "IdentityProvider", tenantId.Value,
            new
            {
                kind = request.Kind.ToString(),
                alias = summary.Alias,
                displayName = summary.DisplayName,
                issuer = summary.Issuer,
                entityId = summary.SamlEntityId,
            });
        await db.SaveChangesAsync(ct);
        return await views.ToDtoAsync(summary, ct);
    }
}
