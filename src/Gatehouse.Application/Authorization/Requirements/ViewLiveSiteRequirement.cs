using Microsoft.AspNetCore.Authorization;

namespace Gatehouse.Application.Authorization.Requirements;

/// <summary>Resource = <see cref="Domain.Sites.SiteId"/>: tenant admin, or security staff whose scope covers the site (authorization.md
/// §9).</summary>
public sealed class ViewLiveSiteRequirement : IAuthorizationRequirement;
