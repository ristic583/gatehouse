using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.LiveBuilding.Events;

public sealed record AlarmAcknowledged(
    AlarmId AlarmId,
    PersonId AcknowledgedById,
    string Comment,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
