using Microsoft.AspNetCore.Authorization;

namespace Gatehouse.Application.Authorization.Requirements;

/// <summary>Resource = <see cref="Domain.Sites.SiteId"/>: security staff whose scope covers the site; tenant admin never (authorization.md
/// §9).</summary>
public sealed class OperateDoorsRequirement : IAuthorizationRequirement;
