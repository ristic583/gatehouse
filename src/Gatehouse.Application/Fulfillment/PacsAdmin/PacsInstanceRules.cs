using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Fulfillment.PacsAdmin;

/// <summary>Rules shared by create and update of a PACS instance.</summary>
public static class PacsInstanceRules
{
    /// <summary>At least one site, all existing (<c>pacs.invalid_site</c>).</summary>
    public static async Task<Result<List<SiteId>>> SitesAsync(ITenantDb db, IReadOnlyList<Guid>? ids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var siteIds = (ids ?? []).Distinct().Select(i => new SiteId(i)).ToList();
        if (siteIds.Count == 0 || await db.Sites.CountAsync(s => siteIds.Contains(s.Id), ct) != siteIds.Count)
        {
            return Error.Validation("pacs.invalid_site", "A PACS instance needs at least one existing site.");
        }

        return siteIds;
    }
}
