using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.People.Export;

/// <summary>
/// <c>GET /api/v1/people/{id}/export</c> (TenantAdmin): a GDPR data export of one person: profile, entitlements, cards (masked),
/// photo metadata, door events of the last 90 days and audit entries about the person. Audits <c>person.exported</c>.
/// </summary>
public sealed class PersonExportHandler(ITenantDb db, ICurrentUser currentUser, IAuditLog audit, TimeProvider time)
{
    public async Task<Result<object>> HandleAsync(PersonId personId, CancellationToken ct)
    {
        var person = await db.Persons.AsNoTracking().FirstOrDefaultAsync(p => p.Id == personId, ct);
        if (person is null)
        {
            return Error.NotFound("person.not_found", "Person not found.");
        }

        var now = time.GetUtcNow();
        var since = now.AddDays(-90);
        var subject = personId.Value.ToString();
        var entitlements = await db.Entitlements.AsNoTracking().Where(e => e.PersonId == personId)
            .Join(
                db.Areas, e => e.AreaId, a => a.Id,
                (e, a) => new { e.Id, Area = a.Name, Status = e.Status.ToString(), e.RequestedStart, e.EffectiveEnd, e.Justification })
            .ToListAsync(ct);
        var cards = (await db.Cards.AsNoTracking().Where(c => c.PersonId == personId).ToListAsync(ct))
            .Select(c => new
            {
                cardId = c.Id.Value,
                cardNumber = CardNumberMask.Mask(c.CardNumber),
                state = c.State.ToString(),
                c.StateChangedAt,
            })
            .ToList();
        var photo = await db.Photos.AsNoTracking().Where(p => p.PersonId == personId)
            .Select(p => new { p.ContentType, p.SizeBytes, p.UploadedAt }).FirstOrDefaultAsync(ct);
        PersonId? nullableId = personId;
        var doorEvents = await db.DoorEvents.AsNoTracking().Where(e => e.PersonId == nullableId && e.OccurredAt >= since)
            .OrderBy(e => e.OccurredAt)
            .Select(e => new
            {
                e.OccurredAt,
                Type = e.Type.ToString(),
                e.PacsDoorRef,
                Direction = e.Direction == null ? null : e.Direction.ToString(),
            })
            .ToListAsync(ct);
        var auditEntries = await db.AuditEntries.AsNoTracking().Where(a => a.SubjectId == subject)
            .OrderBy(a => a.Sequence)
            .Select(a => new { a.Sequence, a.OccurredAt, a.Action, a.ActorKind, a.ActorId })
            .ToListAsync(ct);

        var me = await currentUser.RequirePersonIdAsync(ct);
        audit.Record(
            AuditActor.ForPerson(me), "person.exported", "Person", subject,
            new { doorEvents = doorEvents.Count, auditEntries = auditEntries.Count });
        await db.SaveChangesAsync(ct);

        return new
        {
            exportedAt = now,
            person = new
            {
                personId = person.Id.Value,
                person.GivenName,
                person.FamilyName,
                person.Email,
                type = person.Type.ToString(),
                source = person.Source.ToString(),
                status = person.Status.ToString(),
                eligibilityStart = person.Eligibility.Start,
                eligibilityEnd = person.Eligibility.End,
                person.TerminatedAt,
                person.AnonymizedAt,
            },
            entitlements,
            cards,
            photo,
            doorEvents,
            auditEntries,
        };
    }
}
