namespace Gatehouse.Application.LiveBuilding.Dtos;

/// <summary>Approximate number of people in an area (in/out readers), for the site-local day.</summary>
public sealed record OccupancyDto(Guid AreaId, long Count);
