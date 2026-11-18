using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.People.Events;

public sealed record PersonAnonymized(PersonId PersonId, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
