using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Credentials.Photos;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Application.Sites.Background;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.GetFloorBackground;

/// <summary><c>GET /api/v1/floors/{floorId}/background</c>: the stored drawing, or <c>floor.background_not_found</c>.</summary>
public sealed class GetFloorBackgroundHandler(ITenantDb db, IObjectStore store)
{
    public async Task<Result<FloorBackgroundContent>> HandleAsync(GetFloorBackgroundQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var floor = await db.Floors.AsNoTracking().SingleOrDefaultAsync(f => f.Id == new FloorId(query.FloorId), ct);
        if (floor?.BackgroundObjectKey is null || floor.BackgroundContentType is null || floor.BackgroundUpdatedAt is null)
        {
            return SiteErrors.BackgroundNotFound;
        }

        var bytes = await store.GetAsync(floor.BackgroundObjectKey, ct);
        return bytes is null
            ? SiteErrors.BackgroundNotFound
            : new FloorBackgroundContent(bytes, floor.BackgroundContentType, floor.BackgroundUpdatedAt.Value);
    }
}
