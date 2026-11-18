using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.People.Events;

public sealed record PersonCreated(
    PersonId PersonId,
    PersonType Type,
    PersonSource Source,
    PersonId? ManagerId,
    PersonId? SponsorId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
