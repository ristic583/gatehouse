using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Credentials.Photos;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Application.Sites.Background;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.UploadFloorBackground;

/// <summary>
/// <c>PUT /api/v1/admin/floors/{floorId}/background</c> within <see cref="FloorBackgroundLimits"/>; key
/// <c>tenant/{tenantId}/floors/{floorId}/{guid}.{ext}</c>; replacing deletes the old object; audit <c>floor.background_changed</c>.
/// </summary>
public sealed class UploadFloorBackgroundHandler(
    ITenantDb db,
    ITenantContext tenant,
    IPhotoProcessor processor,
    IObjectStore store,
    AdminAudit audit,
    TimeProvider time)
{
    public async Task<Result> HandleAsync(UploadFloorBackgroundCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Upload);
        var floorId = command.FloorId;
        var floor = await db.Floors.SingleOrDefaultAsync(f => f.Id == new FloorId(floorId), ct);
        if (floor is null)
        {
            return SiteErrors.FloorNotFound;
        }

        var processed = processor.Process(command.Upload, FloorBackgroundLimits.MaxBytes, FloorBackgroundLimits.MaxSourceEdge,
            FloorBackgroundLimits.MaxEdge, FloorBackgroundLimits.ErrorPrefix);
        if (processed.IsFailure)
        {
            return processed.Error;
        }

        var image = processed.Value;
        var extension = image.ContentType == "image/png" ? "png" : "jpg";
        var key = $"tenant/{tenant.TenantId.Value}/floors/{floorId}/{Guid.NewGuid():N}.{extension}";
        var previous = floor.BackgroundObjectKey;
        await store.PutAsync(key, image.Content, image.ContentType, ct);
        floor.SetBackground(key, image.ContentType, image.Width, image.Height, time.GetUtcNow());
        var payload = new { contentType = image.ContentType, widthPx = image.Width, heightPx = image.Height };
        await audit.RecordAsync("floor.background_changed", "Floor", floorId, payload, ct);
        await db.SaveChangesAsync(ct);
        if (previous is not null)
        {
            await store.DeleteAsync(previous, ct);
        }

        return Result.Success();
    }
}
