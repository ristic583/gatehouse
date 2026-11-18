using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Credentials.Photos.DeleteMyPhoto;

/// <summary><c>DELETE /api/v1/me/photo</c>: the object is deleted after commit; audit <c>photo.deleted</c>.</summary>
public sealed class DeleteMyPhotoHandler(ITenantDb db, ICurrentUser currentUser, IObjectStore store, IAuditLog audit)
{
    public async Task<Result> HandleAsync(DeleteMyPhotoCommand command, CancellationToken ct)
    {
        var me = await currentUser.RequirePersonIdAsync(ct);
        var photo = await db.Photos.FirstOrDefaultAsync(p => p.PersonId == me, ct);
        if (photo is null)
        {
            return PhotoErrors.NotFound;
        }

        db.Photos.Remove(photo);
        audit.Record(AuditActor.ForPerson(me), "photo.deleted", "Person", me.Value.ToString(), new { });
        await db.SaveChangesAsync(ct);
        await store.DeleteAsync(photo.ObjectKey, ct);
        return Result.Success();
    }
}
