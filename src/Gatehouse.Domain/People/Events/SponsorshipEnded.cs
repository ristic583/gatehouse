using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.People.Events;

public sealed record SponsorshipEnded(PersonId PersonId, PersonId SponsorId, PersonId? EndedById, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);
