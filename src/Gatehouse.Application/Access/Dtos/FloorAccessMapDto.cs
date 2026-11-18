namespace Gatehouse.Application.Access.Dtos;

public sealed record FloorAccessMapDto(
    Guid FloorId, Guid SiteId, string FloorName, int PlanWidth, int PlanHeight, IReadOnlyList<AreaAccessDto> Areas,
    bool HasBackground = false, DateTimeOffset? BackgroundUpdatedAt = null);
