namespace Gatehouse.Application.LiveBuilding.Dtos;

/// <summary>One push to the live view of a site; exactly the payload matching <see cref="Kind"/> is set.</summary>
public sealed record LiveUpdateDto(
    LiveUpdateKind Kind,
    Guid SiteId,
    LiveDoorEventDto? DoorEvent = null,
    DoorStateDto? DoorState = null,
    AlarmDto? Alarm = null,
    ConnectorHealthDto? Connector = null,
    OccupancyDto? Occupancy = null);
