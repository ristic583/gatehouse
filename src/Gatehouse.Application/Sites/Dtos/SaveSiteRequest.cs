namespace Gatehouse.Application.Sites.Dtos;

public sealed record SaveSiteRequest(string Name, string Code, string TimeZoneId);
