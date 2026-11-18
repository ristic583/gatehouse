using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Application.Identity.IdentityProviders;

/// <summary>Unlinks and deletes the upstream IdP; local admin login keeps working.</summary>
public sealed class RemoveIdentityProviderHandler(
    ITenantContext tenant,
    IdentityProviderStore store,
    IIdentityProvisioner identity,
    ICatalogTenantIdentity catalog,
    ICurrentUser user,
    IAuditLog audit,
    ITenantDb db)
{
    public async Task<Result> HandleAsync(CancellationToken ct)
    {
        var existing = await store.GetAsync(ct);
        if (existing is null)
        {
            return Error.NotFound("idp.not_configured", "No identity provider is configured.");
        }

        var removed = await identity.RemoveProviderAsync(tenant.TenantId, ct);
        if (removed.IsFailure && removed.Error.Code != "idp.not_configured")
        {
            return removed.Error;
        }

        await store.RemoveAsync(ct);
        await catalog.SetIdpKindAsync(tenant.TenantId, IdpKind.Local, ct);
        var actor = AuditActor.ForPerson(await user.RequirePersonIdAsync(ct));
        audit.Record(
            actor, "identity_provider.removed", "IdentityProvider", tenant.TenantId.Value,
            new { kind = existing.Kind.ToString(), alias = existing.Alias });
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
