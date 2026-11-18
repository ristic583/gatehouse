using Gatehouse.Application.Access.Dtos;
using Gatehouse.Domain.LiveBuilding;

namespace Gatehouse.Application.LiveBuilding.GetLiveFloor;

/// <summary><see cref="StateSince"/> is null when the door has no known state (shown as <c>Locked</c>).</summary>
public sealed record LiveDoorDto(
    Guid DoorId,
    Guid AreaId,
    string Name,
    PointDto Position,
    string? PacsDoorRef,
    bool ConnectorManaged,
    DoorState State,
    DateTimeOffset? StateSince,
    IReadOnlyList<LiveReaderDto> Readers,
    bool CanUnlock = false,
    bool CanLockdown = false);
