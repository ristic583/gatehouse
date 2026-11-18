using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.People.Events;

public sealed record PersonUpdated(PersonId PersonId, IReadOnlyList<string> ChangedFields, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);
