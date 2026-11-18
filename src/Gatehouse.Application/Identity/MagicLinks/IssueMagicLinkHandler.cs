using System.Security.Cryptography;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.People.Sponsorship;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Identity;
using Gatehouse.Domain.People;
using Microsoft.Extensions.Configuration;

namespace Gatehouse.Application.Identity.MagicLinks;

/// <summary><c>POST /api/v1/people/{id}/magic-links</c> (<c>ManagePersonRequirement</c>): a single-use 30-minute link for an Active
/// guest.</summary>
public sealed class IssueMagicLinkHandler(
    ITenantDb db,
    ICurrentUser currentUser,
    ITenantContext tenant,
    ManagedPersonLoader loader,
    IMagicLinkTokenCodec codec,
    IAuditLog audit,
    IConfiguration configuration,
    TimeProvider time)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);
    public const string PublicUrlSetting = "Web:PublicUrl";

    public async Task<Result<IssuedMagicLinkDto>> HandleAsync(PersonId personId, CancellationToken ct)
    {
        var person = await loader.LoadAsync(personId, ct);
        if (person.IsFailure)
        {
            return person.Error;
        }

        if (person.Value.Type != PersonType.Guest || person.Value.Status != PersonStatus.Active)
        {
            return Error.Validation("magic_link.guest_only", "Magic links can only be issued for active guests.");
        }

        var me = await currentUser.RequirePersonIdAsync(ct);
        var now = time.GetUtcNow();
        var secret = RandomNumberGenerator.GetBytes(32);
        var link = MagicLink.Issue(personId, Convert.ToHexStringLower(SHA256.HashData(secret)), me, now, Ttl);
        db.MagicLinks.Add(link);
        audit.Record(
            AuditActor.ForPerson(me), "magic_link.issued", "Person", personId.Value.ToString(),
            new { linkId = link.Id.Value, expiresAt = link.ExpiresAt });
        await db.SaveChangesAsync(ct);

        var token = codec.Encode(tenant.TenantId, link.Id, secret);
        CryptographicOperations.ZeroMemory(secret);
        var baseUrl = (configuration[PublicUrlSetting] ?? "http://localhost:3000").TrimEnd('/');
        return new IssuedMagicLinkDto($"{baseUrl}/guest?token={Uri.EscapeDataString(token)}", link.ExpiresAt);
    }
}
