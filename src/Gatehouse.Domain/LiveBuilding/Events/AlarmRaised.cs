using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Domain.LiveBuilding.Events;

public sealed record AlarmRaised(
    AlarmId AlarmId,
    DoorId DoorId,
    AlarmKind Kind,
    Guid DoorEventId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
