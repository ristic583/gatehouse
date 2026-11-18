using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.People.Events;

public sealed record PersonTerminated(PersonId PersonId, DateTimeOffset EligibilityEnd, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);
