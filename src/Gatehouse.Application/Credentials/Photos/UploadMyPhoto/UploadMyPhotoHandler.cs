using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Credentials;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Credentials.Photos.UploadMyPhoto;

/// <summary><c>PUT /api/v1/me/photo</c>: processed, stored, the old object deleted after commit; audit <c>photo.uploaded</c>.</summary>
public sealed class UploadMyPhotoHandler(
    ITenantDb db,
    ICurrentUser currentUser,
    ITenantContext tenant,
    IPhotoProcessor processor,
    IObjectStore store,
    IAuditLog audit,
    TimeProvider time)
{
    public async Task<Result> HandleAsync(UploadMyPhotoCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Upload);
        var me = await currentUser.RequirePersonIdAsync(ct);
        var processed = processor.Process(command.Upload);
        if (processed.IsFailure)
        {
            return processed.Error;
        }

        var now = time.GetUtcNow();
        var photo = processed.Value;
        var extension = photo.ContentType == Photo.Png ? ".png" : ".jpg";
        var key = $"tenant/{tenant.TenantId.Value}/photos/{me.Value}/{Guid.CreateVersion7():N}{extension}";
        var existing = await db.Photos.FirstOrDefaultAsync(p => p.PersonId == me, ct);
        var oldKey = existing?.ObjectKey;
        if (existing is null)
        {
            var created = Photo.Create(me, key, photo.ContentType, photo.Content.Length, now);
            if (created.IsFailure)
            {
                return created.Error;
            }

            db.Photos.Add(created.Value);
        }
        else
        {
            var replaced = existing.Replace(key, photo.ContentType, photo.Content.Length, now);
            if (replaced.IsFailure)
            {
                return replaced;
            }
        }

        await store.PutAsync(key, photo.Content, photo.ContentType, ct);
        var payload = new { sizeBytes = photo.Content.Length, contentType = photo.ContentType };
        audit.Record(AuditActor.ForPerson(me), "photo.uploaded", "Person", me.Value.ToString(), payload);
        await db.SaveChangesAsync(ct);
        if (oldKey is not null)
        {
            await store.DeleteAsync(oldKey, ct);
        }

        return Result.Success();
    }
}
