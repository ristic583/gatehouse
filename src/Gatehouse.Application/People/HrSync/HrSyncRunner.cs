using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using IAuditLog = Gatehouse.Application.Audit.IAuditLog;

namespace Gatehouse.Application.People.HrSync;

/// <summary>
/// Applies the tenant's HR feed (spec §3 identity authority): unknown emails become Authoritative employees, existing Authoritative
/// people are updated, Local people and unresolvable managers are skipped, people missing from the feed are never touched. Managers
/// are resolved after all creates. Saves once. Run per tenant by <c>HrSyncJob</c> and on demand by <see cref="HrSyncAdminHandler"/>.
/// </summary>
public sealed partial class HrSyncRunner(
    ITenantDb db,
    IHrFeedSource source,
    ITenantContext tenant,
    IAuditLog audit,
    ILogger<HrSyncRunner> logger)
{
    public async Task<Result<HrSyncResult>> RunAsync(AuditActor actor, DateTimeOffset now, CancellationToken ct)
    {
        var feed = await source.ReadAsync(tenant.TenantId, ct);
        if (feed is null)
        {
            return Error.Conflict("hr_sync.feed_unavailable", "No HR feed exists for this tenant.");
        }

        var records = feed
            .Where(r => !string.IsNullOrWhiteSpace(r.Email))
            .GroupBy(r => r.Email.Trim().ToLowerInvariant(), StringComparer.Ordinal)
            .Select(g => (Email: g.Key, Record: g.Last()))
            .ToList();
        var feedEmails = records.Select(r => r.Email).ToList();
        var managerEmails = records.Select(r => r.Record.ManagerEmail?.Trim().ToLowerInvariant()).OfType<string>().Distinct().ToList();
        var wanted = feedEmails.Concat(managerEmails).Distinct().ToList();
        var people = await db.Persons.Where(p => wanted.Contains(p.Email)).ToDictionaryAsync(p => p.Email, StringComparer.Ordinal, ct);
        var feedSet = feedEmails.ToHashSet(StringComparer.Ordinal);

        int created = 0, updated = 0, unchanged = 0, skipped = 0;
        var pending = new List<(Person Person, HrFeedRecord Record, Eligibility Eligibility, bool IsNew)>();
        foreach (var (email, record) in records)
        {
            var manager = record.ManagerEmail?.Trim().ToLowerInvariant();
            var eligibility = Eligibility.Create(record.EmploymentStart, record.EmploymentEnd, record.TimeZoneId);
            var managerResolvable = manager is null || people.ContainsKey(manager) || feedSet.Contains(manager);
            people.TryGetValue(email, out var existing);
            if (eligibility.IsFailure || !managerResolvable || existing?.Source == PersonSource.Local)
            {
                skipped++;
                continue;
            }

            if (existing is null)
            {
                var person = Person.Create(
                    PersonSource.Authoritative, PersonType.Employee, record.GivenName, record.FamilyName, email, eligibility.Value,
                    null, null, now);
                if (person.IsFailure)
                {
                    skipped++;
                    continue;
                }

                db.Persons.Add(person.Value);
                people[email] = person.Value;
                pending.Add((person.Value, record, eligibility.Value, true));
            }
            else
            {
                pending.Add((existing, record, eligibility.Value, false));
            }
        }

        foreach (var (person, record, eligibility, isNew) in pending)
        {
            var manager = record.ManagerEmail?.Trim().ToLowerInvariant();
            var managerId = manager is not null && people.TryGetValue(manager, out var m) ? m.Id : (PersonId?)null;
            if (isNew)
            {
                if (managerId is not null)
                {
                    person.SetManager(managerId);
                }

                audit.Record(actor, "person.eligibility_changed", "Person", person.Id.Value.ToString(),
                    new { start = eligibility.Start, end = eligibility.End, previousEnd = (DateTimeOffset?)null });
                created++;
                continue;
            }

            var previous = person.Eligibility;
            var changed = person.UpdateFromHr(record.GivenName, record.FamilyName, managerId, eligibility, now);
            if (changed.IsFailure)
            {
                skipped++;
            }
            else if (changed.Value)
            {
                updated++;
                if (previous.Start != eligibility.Start || previous.End != eligibility.End)
                {
                    audit.Record(actor, "person.eligibility_changed", "Person", person.Id.Value.ToString(),
                        new { start = eligibility.Start, end = eligibility.End, previousEnd = previous.End });
                }
            }
            else
            {
                unchanged++;
            }
        }

        var result = new HrSyncResult(created, updated, unchanged, skipped);
        audit.Record(actor, "hr.sync_completed", "Tenant", tenant.TenantId.Value, new { created, updated, unchanged, skipped });
        await db.SaveChangesAsync(ct);
        if (skipped > 0)
        {
            LogSkipped(skipped);
        }

        return result;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "HR sync skipped {Skipped} feed records (local people, invalid dates or unknown managers)")]
    private partial void LogSkipped(int skipped);
}
