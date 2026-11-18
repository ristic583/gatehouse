using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Domain.LiveBuilding.Events;

public sealed record LiveDriftAlertRaised(
    AlarmId AlarmId,
    PersonId? PersonId,
    AreaId AreaId,
    DoorId DoorId,
    Guid DoorEventId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
