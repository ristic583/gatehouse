using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.ListSites;

public sealed class ListSitesHandler(ITenantDb db)
{
    public async Task<Result<IReadOnlyList<SiteDto>>> HandleAsync(ListSitesQuery query, CancellationToken ct)
    {
        var sites = await db.Sites.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
        var floors = await db.Floors.AsNoTracking().OrderBy(f => f.Level).ToListAsync(ct);
        IReadOnlyList<SiteDto> result = sites
            .Select(s => new SiteDto(
                s.Id.Value,
                s.Name,
                s.Code,
                s.TimeZoneId,
                floors.Where(f => f.SiteId == s.Id).Select(f => new FloorSummaryDto(f.Id.Value, f.Name, f.Level)).ToList()))
            .ToList();
        return Result.Success(result);
    }
}
