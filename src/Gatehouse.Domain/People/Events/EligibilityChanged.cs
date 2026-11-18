using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.People.Events;

public sealed record EligibilityChanged(
    PersonId PersonId,
    DateTimeOffset Start,
    DateTimeOffset? End,
    DateTimeOffset? PreviousEnd,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
