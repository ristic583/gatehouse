using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Access.Events;

public sealed record EntitlementCancelled(
    EntitlementId EntitlementId,
    PersonId CancelledById,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
