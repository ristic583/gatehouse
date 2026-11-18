using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Tasks;

namespace Gatehouse.Domain.Credentials.Events;

public sealed record PinRevealed(PersonId PersonId, PersonId RevealedById, StaffTaskId TaskId, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);
