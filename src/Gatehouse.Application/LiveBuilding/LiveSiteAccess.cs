using System.Security.Claims;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Sites;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.LiveBuilding;

/// <summary>
/// Live hub joins and their revalidation (authorization.md §9): the site must exist in the caller's tenant (SEC-01) and the
/// current user must pass <see cref="ViewLiveSiteRequirement"/>. Tenant-wide staff and admins pass the requirement for any id.
/// </summary>
public sealed class LiveSiteAccess(ITenantDb db, IAuthorizationService authorization)
{
    public async Task<bool> CanViewAsync(SiteId siteId, CancellationToken ct) =>
        await db.Sites.AsNoTracking().AnyAsync(s => s.Id == siteId, ct)
        && (await authorization.AuthorizeAsync(new ClaimsPrincipal(), siteId, new ViewLiveSiteRequirement())).Succeeded;
}
