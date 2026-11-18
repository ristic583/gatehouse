using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Credentials.Photos;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.People.Retention;

/// <summary>
/// GDPR retention for one tenant: deletes photos of people terminated longer than the photo retention, and anonymizes people terminated
/// longer than the personal-data retention (names, email, identity link; PIN credential and magic links deleted). Active people are never
/// touched; audit entries are never deleted.
/// </summary>
public sealed class RetentionRunner(ITenantDb db, RetentionSettingsStore settings, IObjectStore store, IAuditLog audit)
{
    private static readonly AuditActor Actor = AuditActor.ForSystem("retention");
    private static readonly string[] AnonymizedCategories = ["names", "email", "identity", "pin", "magic_links", "photo"];

    public async Task<RetentionRunResult> RunTenantAsync(DateTimeOffset now, CancellationToken ct)
    {
        var retention = await settings.GetAsync(ct);

        var photoCutoff = now.AddDays(-retention.PhotoDaysAfterTermination);
        var expiredPhotos = await db.Photos
            .Join(
                db.Persons.Where(p => p.Status == PersonStatus.Terminated && p.TerminatedAt != null && p.TerminatedAt <= photoCutoff),
                ph => ph.PersonId, p => p.Id, (ph, _) => ph)
            .ToListAsync(ct);
        foreach (var photo in expiredPhotos)
        {
            db.Photos.Remove(photo);
            audit.Record(Actor, "photo.deleted", "Person", photo.PersonId.Value.ToString(), new { reason = "retention" });
        }

        var personalCutoff = now.AddDays(-retention.PersonalDataDaysAfterTermination);
        var people = await db.Persons
            .Where(p => p.Status == PersonStatus.Terminated && p.AnonymizedAt == null && p.TerminatedAt != null
            && p.TerminatedAt <= personalCutoff)
            .ToListAsync(ct);
        var ids = people.Select(p => p.Id).ToList();
        var pins = await db.PinCredentials.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        var links = await db.MagicLinks.Where(l => ids.Contains(l.PersonId)).ToListAsync(ct);
        var photosOfAnonymized = await db.Photos.Where(p => ids.Contains(p.PersonId)).ToListAsync(ct);
        db.PinCredentials.RemoveRange(pins);
        db.MagicLinks.RemoveRange(links);
        foreach (var photo in photosOfAnonymized.Where(p => !expiredPhotos.Contains(p)))
        {
            db.Photos.Remove(photo);
            expiredPhotos.Add(photo);
        }

        foreach (var person in people)
        {
            if (person.Anonymize(now).IsSuccess)
            {
                audit.Record(Actor, "person.anonymized", "Person", person.Id.Value.ToString(), new { categories = AnonymizedCategories });
            }
        }

        await db.SaveChangesAsync(ct);
        foreach (var photo in expiredPhotos)
        {
            await store.DeleteAsync(photo.ObjectKey, ct);
        }

        return new RetentionRunResult(expiredPhotos.Count, people.Count);
    }
}
