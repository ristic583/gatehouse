using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Access.Events;

public sealed record RenewalDue(PersonId PersonId, EntitlementId EntitlementId, DateTimeOffset DueAt, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);
