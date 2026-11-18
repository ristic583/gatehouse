using Gatehouse.Domain.LiveBuilding;

namespace Gatehouse.Application.LiveBuilding.Dtos;

public sealed record AlarmDto(
    Guid AlarmId,
    Guid DoorId,
    string DoorName,
    Guid AreaId,
    string AreaName,
    Guid SiteId,
    AlarmKind Kind,
    DateTimeOffset RaisedAt,
    Guid DoorEventId,
    DateTimeOffset? AcknowledgedAt,
    string? AcknowledgedByName,
    string? Comment);
