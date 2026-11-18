using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Identity;

/// <summary>A single-use sign-in link for a guest (spec §3). Stores only the SHA-256 hash of the secret.</summary>
public sealed class MagicLink
{
    private MagicLink(
        MagicLinkId id, PersonId personId, string tokenHash, DateTimeOffset expiresAt, PersonId createdById, DateTimeOffset createdAt)
    {
        Id = id;
        PersonId = personId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        CreatedById = createdById;
        CreatedAt = createdAt;
    }

    // EF Core
    private MagicLink() => TokenHash = string.Empty;

    public MagicLinkId Id { get; private set; }

    public PersonId PersonId { get; private set; }

    [Sensitive]
    public string TokenHash { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public PersonId CreatedById { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static MagicLink Issue(PersonId personId, string tokenHash, PersonId createdById, DateTimeOffset now, TimeSpan ttl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        return new MagicLink(MagicLinkId.New(), personId, tokenHash, now + ttl, createdById, now);
    }

    public Result Redeem(DateTimeOffset now)
    {
        if (UsedAt is not null || now >= ExpiresAt)
        {
            return Error.Unauthorized("magic_link.invalid", "This link is invalid or has expired.");
        }

        UsedAt = now;
        return Result.Success();
    }
}
