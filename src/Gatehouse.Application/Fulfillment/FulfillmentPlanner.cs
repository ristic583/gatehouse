using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Tasks;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;
using FulfillmentEntity = Gatehouse.Domain.Fulfillment.Fulfillment;

namespace Gatehouse.Application.Fulfillment;

/// <summary>Plans fulfillments for domain changes in-process (ADR-008); callers save once.</summary>
public sealed class FulfillmentPlanner(ITenantDb db, IAuditLog audit, StaffTaskService tasks)
{
    // Planned in-process by design (ADR-008, M4 outcome)
    public async Task<IReadOnlyList<FulfillmentId>> PlanRevocationAsync(
        Entitlement entitlement,
        CommandPriority priority,
        AuditActor actor,
        DateTimeOffset now,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entitlement);
        var grants = await db.Fulfillments
            .Where(f => f.EntitlementId == entitlement.Id && f.Kind == FulfillmentKind.Grant)
            .ToListAsync(ct);
        var created = new List<FulfillmentId>();
        foreach (var grant in grants)
        {
            var status = grant.Status;
            if (status is FulfillmentStatus.Desired or FulfillmentStatus.Pending or FulfillmentStatus.Failed)
            {
                if (grant.Supersede(now).IsSuccess)
                {
                    audit.Record(
                        actor, "fulfillment.superseded", AuditSubjects.Fulfillment, grant.Id.Value.ToString(),
                        new { entitlementId = entitlement.Id.Value });
                    await tasks.ResolveForSubjectAsync("Fulfillment", grant.Id.Value, null, actor, now, ct);
                }
            }

            if (status is FulfillmentStatus.Pending or FulfillmentStatus.Failed or FulfillmentStatus.Confirmed or FulfillmentStatus.Drift)
            {
                created.Add(await PlanRevokeAsync(entitlement, grant.PacsInstanceId, priority, actor, now, ct));
            }
        }

        return created;
    }

    /// <summary>
    /// One revoke per entitlement and instance (unique): a mapping edit may already have used it to move the grant (GH-ACCESS-01). A
    /// desired one goes out as it is, a settled or in-flight one is re-applied (it then removes the level granted last), a failed one
    /// is retried.
    /// </summary>
    private async Task<FulfillmentId> PlanRevokeAsync(
        Entitlement entitlement,
        Domain.Fulfillment.PacsInstanceId instanceId,
        CommandPriority priority,
        AuditActor actor,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var existing = await db.Fulfillments.FirstOrDefaultAsync(
            f => f.EntitlementId == entitlement.Id && f.PacsInstanceId == instanceId && f.Kind == FulfillmentKind.Revoke, ct);
        if (existing is null)
        {
            var revoke = FulfillmentEntity.Create(entitlement.Id, instanceId, FulfillmentKind.Revoke, priority, now);
            db.Fulfillments.Add(revoke);
            return revoke.Id;
        }

        if (existing.Status == FulfillmentStatus.Failed ? existing.Retry(now).IsSuccess : existing.Reapply(priority, now).IsSuccess)
        {
            audit.Record(actor, "fulfillment.retried", AuditSubjects.Fulfillment, existing.Id.Value.ToString(),
                new
                {
                    entitlementId = entitlement.Id.Value,
                    kind = existing.Kind.ToString(),
                    revision = existing.Revision,
                    reason = "ended",
                });
            await tasks.ResolveForSubjectAsync("Fulfillment", existing.Id.Value, null, actor, now, ct);
        }

        return existing.Id;
    }

    /// <summary>Error code of a grant that failed because the person had no card (<c>DispatchFulfillmentHandler</c>).</summary>
    public const string NoActiveCardCode = "fulfillment.no_active_card";

    /// <summary>
    /// Retries the person's grants that failed with <see cref="NoActiveCardCode"/> once a card exists (issued or activated; lab finding
    /// #6),
    /// resolving their staff tasks. Does not save.
    /// </summary>
    public async Task<int> RetryMissingCardAsync(
        Domain.People.PersonId personId, AuditActor actor, DateTimeOffset now, CancellationToken ct)
    {
        var prefix = NoActiveCardCode + ":";
        var failed = await (from f in db.Fulfillments
                            join e in db.Entitlements on f.EntitlementId equals e.Id
                            where e.PersonId == personId && e.Status == EntitlementStatus.Approved
                                && f.Kind == FulfillmentKind.Grant && f.Status == FulfillmentStatus.Failed && f.LastError != null
                                && f.LastError.StartsWith(prefix)
                            select f).ToListAsync(ct);
        var retried = 0;
        foreach (var fulfillment in failed)
        {
            if (fulfillment.Retry(now).IsFailure)
            {
                continue;
            }

            audit.Record(actor, "fulfillment.retried", AuditSubjects.Fulfillment, fulfillment.Id.Value.ToString(),
                new
                {
                    entitlementId = fulfillment.EntitlementId.Value,
                    kind = fulfillment.Kind.ToString(),
                    revision = fulfillment.Revision,
                    reason = "card_available",
                });
            await tasks.ResolveForSubjectAsync("Fulfillment", fulfillment.Id.Value, null, actor, now, ct);
            retried++;
        }

        return retried;
    }
}
