using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Application.LiveBuilding.Dtos;

/// <summary>A door event as shown in the live view; never carries the card hash.</summary>
public sealed record LiveDoorEventDto(
    Guid EventId,
    DateTimeOffset OccurredAt,
    DoorEventType Type,
    Guid? DoorId,
    string? DoorName,
    Guid? AreaId,
    string? AreaName,
    string? ReaderName,
    Guid? PersonId,
    string? PersonDisplayName,
    ReaderDirection? Direction);
