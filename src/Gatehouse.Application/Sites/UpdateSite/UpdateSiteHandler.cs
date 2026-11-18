using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.UpdateSite;

/// <summary><c>PUT /api/v1/admin/sites/{siteId}</c>; audit <c>site.updated</c>.</summary>
public sealed class UpdateSiteHandler(ITenantDb db, AdminAudit audit)
{
    public async Task<Result<SiteDto>> HandleAsync(UpdateSiteCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;
        ArgumentNullException.ThrowIfNull(request);
        var id = new SiteId(command.SiteId);
        var site = await db.Sites.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (site is null)
        {
            return SiteErrors.SiteNotFound;
        }

        var name = request.Name?.Trim() ?? string.Empty;
        var code = request.Code?.Trim() ?? string.Empty;
        var unique = await SiteUniqueness.CheckAsync(db, name, code, id, ct);
        if (unique.IsFailure)
        {
            return unique.Error;
        }

        var updated = site.Update(name, code, request.TimeZoneId?.Trim() ?? string.Empty);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await audit.RecordAsync("site.updated", "Site", command.SiteId, new { name, code, timeZoneId = site.TimeZoneId }, ct);
        await db.SaveChangesAsync(ct);
        var floors = await db.Floors.AsNoTracking().Where(f => f.SiteId == id).OrderBy(f => f.Level).ToListAsync(ct);
        return SiteAdminMapper.ToDto(site, floors);
    }
}
