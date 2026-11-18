namespace Gatehouse.Application.Sites.Dtos;

/// <summary>Floor as returned by the admin floor routes (the editor reads <c>FloorPlanDto</c> from <c>/plan</c>).</summary>
public sealed record AdminFloorDto(Guid FloorId, Guid SiteId, string Name, int Level, int PlanWidth, int PlanHeight);
