namespace Gatehouse.Application.Sites.Dtos;

public sealed record FloorPlanDto(
    Guid FloorId,
    Guid SiteId,
    string Name,
    int Level,
    int PlanWidth,
    int PlanHeight,
    IReadOnlyList<PlanAreaDto> Areas,
    bool HasBackground,
    DateTimeOffset? BackgroundUpdatedAt);
