using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Application.Identity.IdentityProviders;

/// <summary>Runs the checks without saving; records the result on the saved summary when the request describes the saved IdP.</summary>
public sealed class TestIdentityProviderHandler(
    IdentityProviderChecks checks,
    IdentityProviderStore store,
    ICurrentUser user,
    IAuditLog audit,
    ITenantDb db,
    TimeProvider time)
{
    public async Task<Result<IdpTestResultDto>> HandleAsync(SaveIdentityProviderRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var outcome = await checks.RunAsync(request, ct);
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        var result = outcome.Value.Result;
        var saved = await store.GetAsync(ct);
        if (saved is not null && IsSame(saved, request, outcome.Value))
        {
            await store.SetAsync(saved with { LastTestedAt = time.GetUtcNow(), LastTestOk = result.Ok }, time.GetUtcNow(), ct);
        }

        var actor = AuditActor.ForPerson(await user.RequirePersonIdAsync(ct));
        audit.Record(
            actor, "identity_provider.tested", "IdentityProvider", user.TenantId.Value,
            new { kind = request.Kind.ToString(), ok = result.Ok });
        await db.SaveChangesAsync(ct);
        return result;
    }

    private static bool IsSame(TenantIdentityProvider saved, SaveIdentityProviderRequest request, IdpCheckOutcome outcome) =>
        saved.Kind == request.Kind && request.Kind switch
        {
            IdpKind.Oidc => string.Equals(saved.DiscoveryUrl, request.Oidc?.DiscoveryUrl.Trim(), StringComparison.Ordinal)
                && string.Equals(saved.ClientId, request.Oidc?.ClientId.Trim(), StringComparison.Ordinal),
            IdpKind.Saml => outcome.Saml is not null && string.Equals(saved.SamlEntityId, outcome.Saml.EntityId, StringComparison.Ordinal),
            _ => false,
        };
}
