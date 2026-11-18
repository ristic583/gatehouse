using Gatehouse.Application.Sites.Dtos;

namespace Gatehouse.Application.Sites.UpdateSite;

public sealed record UpdateSiteCommand(Guid SiteId, SaveSiteRequest Request);
