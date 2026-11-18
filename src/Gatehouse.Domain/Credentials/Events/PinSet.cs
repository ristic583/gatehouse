using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Credentials.Events;

public sealed record PinSet(PersonId PersonId, PersonId SetById, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
