using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Domain.Access.Events;

public sealed record EntitlementRevoked(
    EntitlementId EntitlementId,
    PersonId PersonId,
    AreaId AreaId,
    PersonId? RevokedById,
    string Reason,
    RevocationKind Kind,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
