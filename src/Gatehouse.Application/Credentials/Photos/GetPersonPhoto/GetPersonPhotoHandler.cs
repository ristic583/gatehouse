using Gatehouse.Application.Audit;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Credentials.ListCards;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Credentials.Photos.GetPersonPhoto;

/// <summary>
/// <c>GET /api/v1/people/{personId}/photo</c>: self, people who manage the person (<c>ManagePersonRequirement</c>) and security
/// staff who can see the person. Others get <c>404 photo.not_found</c>; views of someone else are audited (<c>photo.viewed</c>).
/// </summary>
public sealed class GetPersonPhotoHandler(ITenantDb db, ICurrentUser currentUser, CardAccess access, IObjectStore store, IAuditLog audit)
{
    public async Task<Result<PhotoContent>> HandleAsync(GetPersonPhotoQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var personId = query.PersonId;
        var me = await currentUser.RequirePersonIdAsync(ct);
        if (me != personId && !await MayViewAsync(personId, ct))
        {
            return PhotoErrors.NotFound;
        }

        var photo = await db.Photos.AsNoTracking().FirstOrDefaultAsync(p => p.PersonId == personId, ct);
        var content = photo is null ? null : await store.GetAsync(photo.ObjectKey, ct);
        if (photo is null || content is null)
        {
            return PhotoErrors.NotFound;
        }

        if (me != personId)
        {
            audit.Record(AuditActor.ForPerson(me), "photo.viewed", "Person", personId.Value.ToString(), new { });
            await db.SaveChangesAsync(ct);
        }

        return new PhotoContent(content, photo.ContentType);
    }

    private async Task<bool> MayViewAsync(PersonId personId, CancellationToken ct)
    {
        var person = await db.Persons.VisibleTo(currentUser).AsNoTracking().FirstOrDefaultAsync(p => p.Id == personId, ct);
        return person is not null
            && (currentUser.IsSecurityStaff || await access.AllowedAsync(CardAccess.ResourceFor(person), new ManagePersonRequirement()));
    }
}
