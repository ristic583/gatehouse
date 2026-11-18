using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Access;

public sealed record Revocation(PersonId? RevokedById, DateTimeOffset RevokedAt, string Reason, RevocationKind Kind);
