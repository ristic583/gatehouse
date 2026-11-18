using Gatehouse.Application.Access.Renewal;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Fulfillment;
using Gatehouse.Application.People.Termination;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.People.Lifecycle;

/// <summary>
/// One lifecycle pass for the current tenant: terminations (one save per person), expiries with revoke fulfillments (one save), then
/// renewals.
/// </summary>
public sealed class LifecycleRunner(
    ITenantDb db,
    TerminationService termination,
    RenewalScheduler renewals,
    LifecycleSettingsStore settings,
    FulfillmentPlanner planner,
    IAuditLog audit,
    Access.Expiry.UnapprovedRequestExpiry unapproved)
{
    private static readonly AuditActor TerminationActor = AuditActor.ForSystem("termination");
    private static readonly AuditActor ExpiryActor = AuditActor.ForSystem("expiry");

    public async Task<LifecycleRunResult> RunTenantAsync(DateTimeOffset now, CancellationToken ct)
    {
        var ending = await db.Persons
            .Where(p => p.Status == PersonStatus.Active && p.Eligibility.End != null && p.Eligibility.End <= now)
            .Select(p => p.Id)
            .ToListAsync(ct);
        var terminated = 0;
        foreach (var personId in ending)
        {
            var person = await db.Persons.SingleAsync(p => p.Id == personId, ct);
            var outcome = await termination.TerminateAsync(person, TerminationActor, now, ct);
            if (outcome.IsFailure)
            {
                continue;
            }

            await db.SaveChangesAsync(ct);
            await termination.NotifyAsync(outcome.Value, ct);
            terminated++;
        }

        var expiring = await db.Entitlements.Where(e => e.Status == EntitlementStatus.Approved && e.EffectiveEnd <= now).ToListAsync(ct);
        var expired = 0;
        foreach (var entitlement in expiring)
        {
            if (entitlement.Expire(now).IsFailure)
            {
                continue;
            }

            // Explicit revoke: a PACS without validity windows would otherwise keep the access (lab finding #2).
            var revokeIds = await planner.PlanRevocationAsync(entitlement, CommandPriority.Normal, ExpiryActor, now, ct);
            audit.Record(ExpiryActor, "entitlement.expired", AuditSubjects.Entitlement, entitlement.Id.Value.ToString(),
                new { revokeFulfillmentIds = revokeIds.Select(r => r.Value).ToList() });
            expired++;
        }

        if (expired > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        // Pending requests whose window ended leave the inboxes (M11-17); not counted in the result.
        await unapproved.RunAsync(now, ct);
        var requested = await renewals.RunAsync(await settings.GetAsync(ct), now, ct);
        return new LifecycleRunResult(terminated, expired, requested);
    }
}
