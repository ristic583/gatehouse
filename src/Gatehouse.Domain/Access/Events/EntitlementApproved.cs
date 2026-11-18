using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Domain.Access.Events;

public sealed record EntitlementApproved(
    EntitlementId EntitlementId,
    PersonId PersonId,
    AreaId AreaId,
    DateTimeOffset Start,
    DateTimeOffset EffectiveEnd,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
