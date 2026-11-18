using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Application.Access.Renewal;

/// <summary>
/// Renewal step of <c>LifecycleRunner</c>: opens renewal requests (never renews automatically). For each Approved entitlement of an
/// Active person, <c>dueAt = min(EffectiveEnd, RequestedStart + RenewalDays(type))</c>; inside the lead window it opens a normal
/// access request (requester = subject, same approver chain as <c>RequestAccessHandler</c>) from <c>dueAt</c>, capped at the
/// eligibility end. Saves once.
/// </summary>
public sealed partial class RenewalScheduler(ITenantDb db, IAuditLog audit, ILogger<RenewalScheduler> logger)
{
    private static readonly AuditActor Actor = AuditActor.ForSystem("renewal-scheduler");

    public async Task<int> RunAsync(LifecycleSettings settings, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var lead = TimeSpan.FromDays(settings.RenewalLeadDays);
        var candidates = await db.Entitlements
            .Where(e => e.Status == EntitlementStatus.Approved && e.EffectiveEnd > now)
            .Join(
                db.Persons.Where(p => p.Status == PersonStatus.Active),
                e => e.PersonId,
                p => p.Id,
                (e, p) => new { Entitlement = e, Person = p })
            .ToListAsync(ct);

        var requested = 0;
        foreach (var candidate in candidates)
        {
            var (entitlement, person) = (candidate.Entitlement, candidate.Person);
            var interval = settings.RenewalDays(person.Type);
            var byInterval = entitlement.RequestedStart.AddDays(interval);
            var dueAt = entitlement.EffectiveEnd < byInterval ? entitlement.EffectiveEnd : byInterval;
            if (now < dueAt - lead || dueAt <= now || (person.Eligibility.End is { } end && end <= dueAt))
            {
                continue;
            }

            var from = dueAt - lead;
            var alreadyOpen = await db.Entitlements.AnyAsync(
                e => e.PersonId == person.Id && e.AreaId == entitlement.AreaId && e.Id != entitlement.Id
                    && (e.Status == EntitlementStatus.PendingApproval || e.Status == EntitlementStatus.Approved)
                    && e.RequestedStart >= from,
                ct);
            if (alreadyOpen)
            {
                continue;
            }

            if (await TryOpenAsync(entitlement, person, interval, dueAt, now, ct))
            {
                requested++;
            }
        }

        if (requested > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return requested;
    }

    private async Task<bool> TryOpenAsync(
        Entitlement old, Person person, int interval, DateTimeOffset dueAt, DateTimeOffset now, CancellationToken ct)
    {
        var area = await db.Areas.AsNoTracking().SingleAsync(a => a.Id == old.AreaId, ct);
        var level = await db.SecurityLevels.AsNoTracking().SingleAsync(l => l.Id == area.SecurityLevelId, ct);
        var mapped = await db.AccessLevelMappings.AnyAsync(m => m.AreaId == area.Id, ct);
        var existing = await db.Entitlements.AsNoTracking()
            .Where(e => e.PersonId == person.Id && e.AreaId == area.Id && e.Id != old.Id)
            .Select(e => new { e.Status, e.EffectiveEnd })
            .ToListAsync(ct);

        var end = dueAt.AddDays(Math.Min(interval, level.MaxDurationDays));
        if (person.Eligibility.End is { } eligibilityEnd && eligibilityEnd < end)
        {
            end = eligibilityEnd;
        }

        var justification = RenewalJustification(area.Name, old);
        var decision = AccessRequestPolicy.Evaluate(new AccessRequestInput(
            person.Status, person.Eligibility, level.MaxDurationDays, mapped, existing.Select(e => (e.Status, e.EffectiveEnd)).ToList(),
            dueAt, end, justification, now));
        if (decision.IsFailure)
        {
            LogSkipped(old.Id.Value, decision.Error.Code);
            return false;
        }

        var steps = ApproverResolver.Resolve(
            ApprovalRouter.BuildSteps(level.ApproverChain),
            new PersonSnapshot(person.Id, person.ManagerId, person.SponsorId),
            person.Id,
            person.ManagerId,
            area.Owners.Select(o => o.PersonId).ToHashSet(),
            await db.RoleAssignments.AsNoTracking().Where(r => r.Role == AssignedRole.TenantAdmin)
                .OrderBy(r => r.PersonId).Select(r => r.PersonId).Distinct().ToListAsync(ct));
        if (steps.IsFailure)
        {
            LogSkipped(old.Id.Value, steps.Error.Code);
            return false;
        }

        var renewal = Entitlement.Create(
            person.Id, area.Id, person.Id, dueAt, end, decision.Value.EffectiveEnd, justification, steps.Value, now);
        db.Entitlements.Add(renewal);
        old.MarkRenewalDue(dueAt, now);
        audit.Record(Actor, "entitlement.renewal_requested", AuditSubjects.Entitlement, renewal.Id.Value.ToString(),
            new { renewalOf = old.Id.Value.ToString(), dueAt });
        return true;
    }

    /// <summary>Names the original request for the approver (lab finding #20), within the 500-character justification limit.</summary>
    internal static string RenewalJustification(string areaName, Entitlement old)
    {
        var text = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Renewal of {areaName} access ({old.RequestedStart:yyyy-MM-dd} to {old.EffectiveEnd:yyyy-MM-dd}). "
            + $"Original reason: {old.Justification}");
        return text.Length <= 500 ? text : text[..497] + "...";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Renewal of entitlement {EntitlementId} skipped: {ErrorCode}")]
    private partial void LogSkipped(Guid entitlementId, string errorCode);
}
