namespace Gatehouse.Application.Sites.Dtos;

public sealed record SiteDto(Guid SiteId, string Name, string Code, string TimeZoneId, IReadOnlyList<FloorSummaryDto> Floors);
