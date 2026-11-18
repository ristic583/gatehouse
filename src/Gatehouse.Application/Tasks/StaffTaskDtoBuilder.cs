using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Application.Tasks.Dtos;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Reconciliation;
using Gatehouse.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Tasks;

public sealed class StaffTaskDtoBuilder(ITenantDb db)
{
    public async Task<IReadOnlyList<StaffTaskDto>> BuildAsync(IReadOnlyList<StaffTask> tasks, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        if (tasks.Count == 0)
        {
            return [];
        }

        var personIds = tasks.SelectMany(t => new[] { t.AssignedToId, t.ResolvedById }).OfType<PersonId>().Distinct().ToList();
        var people = await db.Persons.AsNoTracking()
            .Where(p => personIds.Contains(p.Id))
            .Select(p => new { p.Id, Name = p.GivenName + " " + p.FamilyName })
            .ToDictionaryAsync(p => p.Id, p => new PersonRefDto(p.Id.Value, p.Name), ct);

        var siteIds = tasks.Select(t => t.SiteId).OfType<Gatehouse.Domain.Sites.SiteId>().Distinct().ToList();
        var sites = await db.Sites.AsNoTracking().Where(s => siteIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var fulfillmentIds = tasks.Where(t => t.SubjectType == "Fulfillment").Select(t => new FulfillmentId(t.SubjectId)).ToList();
        var summaries = (await db.Fulfillments.AsNoTracking()
                .Where(f => fulfillmentIds.Contains(f.Id))
                .Join(db.Entitlements, f => f.EntitlementId, e => e.Id, (f, e) => new { f.Id, f.Kind, e.PersonId, e.AreaId })
                .Join(
                    db.Persons, x => x.PersonId, p => p.Id,
                    (x, p) => new { x.Id, x.Kind, x.AreaId, Person = p.GivenName + " " + p.FamilyName })
                .Join(db.Areas, x => x.AreaId, a => a.Id, (x, a) => new { x.Id, x.Kind, x.Person, Area = a.Name })
                .ToListAsync(ct))
            .ToDictionary(x => x.Id.Value, x => (x.Kind, x.Person, x.Area));

        var pinSubjects = tasks.Where(t => t.Kind == StaffTaskKind.PinReveal).Select(t => new PersonId(t.SubjectId)).Distinct().ToList();
        var pinPeople = pinSubjects.Count == 0
            ? []
            : await db.Persons.AsNoTracking().Where(p => pinSubjects.Contains(p.Id))
                .Select(p => new { p.Id, Name = p.GivenName + " " + p.FamilyName })
                .ToDictionaryAsync(p => p.Id.Value, p => p.Name, ct);

        var findings = await FindingSummariesAsync(tasks, ct);
        var alarmAreas = await AlarmAreasAsync(tasks, ct);

        PersonRefDto? Ref(PersonId? id) => id is { } v && people.TryGetValue(v, out var p) ? p : null;

        return tasks.Select(t => new StaffTaskDto(
            t.Id.Value,
            t.Kind.ToString(),
            t.Status.ToString(),
            t.SiteId?.Value,
            t.SiteId is { } s && sites.TryGetValue(s, out var name) ? name : null,
            t.SubjectType,
            t.SubjectId,
            Summary(t, summaries, findings, alarmAreas, pinPeople),
            Ref(t.AssignedToId),
            t.ResolutionAction?.ToString(),
            t.ResolutionNote,
            Ref(t.ResolvedById),
            t.ResolvedAt,
            t.CreatedAt,
            t.Kind == StaffTaskKind.ReconciliationFinding && findings.TryGetValue(t.SubjectId, out var finding)
                ? finding.Kind.ToString()
                : null,
            t.Kind == StaffTaskKind.PinReveal && pinPeople.TryGetValue(t.SubjectId, out var pinPerson) ? pinPerson : null)).ToList();
    }

    private static string Summary(
        StaffTask t,
        Dictionary<Guid, (FulfillmentKind Kind, string Person, string Area)> summaries,
        Dictionary<Guid, (FindingKind Kind, string Person, string? Area)> findings,
        Dictionary<Guid, string> alarmAreas,
        Dictionary<Guid, string> pinPeople)
    {
        if (t.Kind == StaffTaskKind.PinReveal)
        {
            return pinPeople.TryGetValue(t.SubjectId, out var person) ? $"PIN help: {person}" : "PIN help";
        }

        if (t.SubjectType == "Fulfillment" && summaries.TryGetValue(t.SubjectId, out var f))
        {
            return t.Kind == StaffTaskKind.RevocationUnconfirmed
                ? $"Revocation not confirmed: {f.Person} → {f.Area}"
                : $"{f.Kind} failed: {f.Person} → {f.Area}";
        }

        if (t.Kind == StaffTaskKind.ReconciliationFinding && findings.TryGetValue(t.SubjectId, out var finding))
        {
            var label = finding.Kind switch
            {
                FindingKind.MissingInPacs => "Missing in PACS",
                FindingKind.ExtraInPacs => "Extra in PACS",
                FindingKind.Mismatch => "Mismatch",
                _ => "Expired but still in PACS",
            };
            return finding.Area is null ? $"{label}: {finding.Person}" : $"{label}: {finding.Person} · {finding.Area}";
        }

        if (t.Kind == StaffTaskKind.ScheduledRemovalRefused)
        {
            return "Scheduled access removal refused by the PACS";
        }

        if (t.Kind == StaffTaskKind.DriftAlert && alarmAreas.TryGetValue(t.SubjectId, out var area))
        {
            return $"Access without entitlement at {area} door";
        }

        return t.Kind.ToString();
    }

    private async Task<Dictionary<Guid, (FindingKind Kind, string Person, string? Area)>> FindingSummariesAsync(
        IReadOnlyList<StaffTask> tasks, CancellationToken ct)
    {
        var ids = tasks.Where(t => t.Kind == StaffTaskKind.ReconciliationFinding)
            .Select(t => new ReconciliationFindingId(t.SubjectId))
            .ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await (from f in db.ReconciliationFindings.AsNoTracking()
                          where ids.Contains(f.Id)
                          join p in db.Persons.AsNoTracking() on f.PersonId equals (PersonId?)p.Id into ps
                          from p in ps.DefaultIfEmpty()
                          join a in db.Areas.AsNoTracking() on f.AreaId equals (Gatehouse.Domain.Sites.AreaId?)a.Id into areas
                          from a in areas.DefaultIfEmpty()
                          select new
                          {
                              f.Id,
                              f.Kind,
                              f.PersonRef,
                              Person = p == null ? null : p.GivenName + " " + p.FamilyName,
                              Area = a == null ? null : a.Name,
                          })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id.Value, r => (r.Kind, r.Person ?? r.PersonRef ?? "Unknown person", (string?)r.Area));
    }

    private async Task<Dictionary<Guid, string>> AlarmAreasAsync(IReadOnlyList<StaffTask> tasks, CancellationToken ct)
    {
        var ids = tasks.Where(t => t.Kind == StaffTaskKind.DriftAlert)
            .Select(t => new Gatehouse.Domain.LiveBuilding.AlarmId(t.SubjectId))
            .ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await (from al in db.Alarms.AsNoTracking()
                      where ids.Contains(al.Id)
                      join d in db.Doors.AsNoTracking() on al.DoorId equals d.Id
                      join a in db.Areas.AsNoTracking() on d.AreaId equals a.Id
                      select new { al.Id, a.Name })
            .ToDictionaryAsync(x => x.Id.Value, x => x.Name, ct);
    }
}
