using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Domain.Access.Events;

public sealed record AccessRequested(
    EntitlementId EntitlementId,
    PersonId PersonId,
    AreaId AreaId,
    PersonId RequestedById,
    DateTimeOffset RequestedStart,
    DateTimeOffset RequestedEnd,
    DateTimeOffset EffectiveEnd,
    bool CappedToEligibility,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
