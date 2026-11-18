using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Domain.Access.Events;

public sealed record EntitlementRejected(
    EntitlementId EntitlementId,
    PersonId PersonId,
    AreaId AreaId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
