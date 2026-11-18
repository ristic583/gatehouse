using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.DeleteSite;

/// <summary><c>DELETE /api/v1/admin/sites/{siteId}</c> (<c>domain-model.md</c> §2.10 deletion rules); audit <c>site.deleted</c>.</summary>
public sealed class DeleteSiteHandler(ITenantDb db, AdminAudit audit)
{
    public async Task<Result> HandleAsync(DeleteSiteCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var id = new SiteId(command.SiteId);
        var site = await db.Sites.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (site is null)
        {
            return SiteErrors.SiteNotFound;
        }

        if (await db.Floors.AnyAsync(f => f.SiteId == id, ct))
        {
            return Error.Conflict("site.not_empty", "Delete the site's floors first.");
        }

        var instances = await db.PacsInstances.AsNoTracking().ToListAsync(ct);
        if (instances.Any(i => i.SiteIds.Contains(id)))
        {
            return Error.Conflict("site.not_empty", "A PACS instance covers this site.");
        }

        db.Sites.Remove(site);
        await audit.RecordAsync("site.deleted", "Site", command.SiteId, new { name = site.Name, code = site.Code }, ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
