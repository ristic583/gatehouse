using Gatehouse.Application.LiveBuilding.GetLiveFloor;

namespace Gatehouse.Application.LiveBuilding.Dtos;

/// <summary>Live snapshot of one floor: doors with state, open alarms of its doors, the site's last 50 events (newest first).</summary>
public sealed record LiveFloorDto(
    Guid FloorId,
    Guid SiteId,
    string SiteName,
    string FloorName,
    int PlanWidth,
    int PlanHeight,
    IReadOnlyList<LiveAreaDto> Areas,
    IReadOnlyList<LiveDoorDto> Doors,
    IReadOnlyList<AlarmDto> OpenAlarms,
    IReadOnlyList<LiveDoorEventDto> RecentEvents,
    IReadOnlyList<OccupancyDto>? Occupancy = null,
    bool HasBackground = false,
    DateTimeOffset? BackgroundUpdatedAt = null);
