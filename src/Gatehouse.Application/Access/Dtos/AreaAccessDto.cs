namespace Gatehouse.Application.Access.Dtos;

public sealed record AreaAccessDto(
    Guid AreaId,
    string Name,
    string Code,
    IReadOnlyList<PointDto> Polygon,
    string SecurityLevelName,
    string State,
    string Reason,
    bool CardInactive,
    DateTimeOffset? Since,
    IReadOnlyList<DoorMarkerDto> Doors);
