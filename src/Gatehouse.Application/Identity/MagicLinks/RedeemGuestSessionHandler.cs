using System.Security.Cryptography;
using System.Text;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Identity;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.Application.Identity.MagicLinks;

/// <summary>
/// <c>POST /api/v1/guest/sessions</c> (anonymous, rate limited): verifies the signed token, then in that tenant's scope matches the link
/// hash
/// in constant time, redeems it once (row version) for an Active guest and issues a 30-minute guest token. Every failure is the same
/// <c>401 magic_link.invalid</c>.
/// </summary>
public sealed class RedeemGuestSessionHandler(
    IMagicLinkTokenCodec codec,
    ICatalogReader catalog,
    ITenantScopeFactory scopes,
    IGuestTokenIssuer issuer,
    TimeProvider time)
{
    private static readonly byte[] DummyHash = new byte[32];

    public static Error Invalid() => Error.Unauthorized("magic_link.invalid", "This link is invalid or has expired.");

    public async Task<Result<GuestSessionDto>> HandleAsync(GuestSessionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var token = codec.TryDecode(request.Token);
        if (token is null)
        {
            CryptographicOperations.FixedTimeEquals(DummyHash, SHA256.HashData(DummyHash));
            return Invalid();
        }

        var tenant = await catalog.FindAsync(token.TenantId, ct);
        if (tenant is null || tenant.Status != TenantStatus.Active)
        {
            return Invalid();
        }

        await using var scope = await scopes.CreateScopeAsync(token.TenantId, ct);
        var db = scope.ServiceProvider.GetRequiredService<ITenantDb>();
        var link = await db.MagicLinks.FirstOrDefaultAsync(l => l.Id == token.LinkId, ct);
        var presented = SHA256.HashData(token.Secret);
        var stored = link is null ? DummyHash : Convert.FromHexString(link.TokenHash);
        if (!CryptographicOperations.FixedTimeEquals(presented, stored) || link is null)
        {
            return Invalid();
        }

        var now = time.GetUtcNow();
        var person = await db.Persons.AsNoTracking().FirstOrDefaultAsync(p => p.Id == link.PersonId, ct);
        if (person is null || person.Type != PersonType.Guest || person.Status != PersonStatus.Active || link.Redeem(now).IsFailure)
        {
            return Invalid();
        }

        scope.ServiceProvider.GetRequiredService<IAuditLog>()
            .Record(
                AuditActor.ForSystem("magic-link"), "magic_link.redeemed", "Person", person.Id.Value.ToString(),
                new { linkId = link.Id.Value });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Invalid();
        }

        var issued = issuer.Issue(person, token.TenantId, now);
        return new GuestSessionDto(
            issued.AccessToken, issued.ExpiresAt, issued.Subject, person.Email, person.DisplayName, token.TenantId.Value);
    }
}
