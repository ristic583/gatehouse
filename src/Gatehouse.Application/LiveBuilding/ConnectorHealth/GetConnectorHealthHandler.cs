using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.LiveBuilding.ConnectorHealth;

/// <summary>PACS instances covering sites the caller may view live (<c>ViewLiveSiteRequirement</c> semantics).</summary>
public sealed class GetConnectorHealthHandler(ITenantDb db, IConnectorHealthCache cache, ICurrentUser user, TimeProvider time)
{
    public async Task<Result<IReadOnlyList<ConnectorHealthDto>>> HandleAsync(GetConnectorHealthQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var instances = (await db.PacsInstances.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct))
            .Where(i => i.SiteIds.Any(CanView) && (query.SiteId is not { } s || i.SiteIds.Contains(new SiteId(s))))
            .ToList();
        return Result.Success<IReadOnlyList<ConnectorHealthDto>>(
            await ConnectorHealthProjection.BuildAsync(db, cache, instances, time.GetUtcNow(), ct));
    }

    private bool CanView(SiteId siteId) =>
        user.IsTenantAdmin || (user.IsSecurityStaff && (user.SecurityStaffSiteIds is not { } sites || sites.Contains(siteId)));
}
