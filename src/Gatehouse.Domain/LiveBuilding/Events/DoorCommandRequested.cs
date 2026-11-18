using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.LiveBuilding.Events;

public sealed record DoorCommandRequested(
    DoorCommandId DoorCommandId,
    DoorCommandKind Kind,
    DoorCommandScope Scope,
    Guid ScopeId,
    PersonId RequestedById,
    string Reason,
    int? DurationSeconds,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
