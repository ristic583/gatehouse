using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Tasks;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Gatehouse.Application.Fulfillment.MonitorRevocations;

/// <summary>Flags revoke fulfillments that stayed unconfirmed past the threshold; returns the number of new alerts.</summary>
public sealed class DetectUnconfirmedRevocationsHandler(
    ITenantDb db,
    StaffTaskService tasks,
    IAuditLog audit,
    IOptions<RevocationMonitorOptions> options)
{
    public async Task<int> HandleAsync(DetectUnconfirmedRevocationsCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = command.Now;
        var opts = options.Value;
        var actor = AuditActor.ForSystem("revocation-monitor");

        var candidates = await (
            from f in db.Fulfillments
            join e in db.Entitlements on f.EntitlementId equals e.Id
            where f.Kind == FulfillmentKind.Revoke
                && f.Status != FulfillmentStatus.Confirmed
                && f.Status != FulfillmentStatus.Superseded
                && e.Status == EntitlementStatus.Revoked
            select new { Fulfillment = f, Entitlement = e })
            .ToListAsync(ct);

        var count = 0;
        foreach (var c in candidates)
        {
            var revocation = c.Entitlement.Revocation;
            if (revocation is null)
            {
                continue;
            }

            var threshold = revocation.Kind == RevocationKind.Emergency ? opts.EmergencyAlertAfter : opts.AlertAfter;
            if (revocation.RevokedAt > now - threshold)
            {
                continue;
            }

            var fulfillmentId = c.Fulfillment.Id.Value;
            var alreadyOpen = await db.StaffTasks.AnyAsync(
                t =>
                    t.Kind == StaffTaskKind.RevocationUnconfirmed && t.SubjectId == fulfillmentId
                        && t.Status != StaffTaskStatus.Resolved, ct);
            if (alreadyOpen || c.Fulfillment.FlagRevocationUnconfirmed(revocation.RevokedAt, now).IsFailure)
            {
                continue;
            }

            var siteId = await (
                from a in db.Areas
                join fl in db.Floors on a.FloorId equals fl.Id
                where a.Id == c.Entitlement.AreaId
                select (Gatehouse.Domain.Sites.SiteId?)fl.SiteId)
                .FirstOrDefaultAsync(ct);

            await tasks.OpenAsync(StaffTaskKind.RevocationUnconfirmed, siteId, "Fulfillment", fulfillmentId, actor, now, ct);
            audit.Record(
                actor,
                "revocation.unconfirmed_alert",
                AuditSubjects.Fulfillment,
                fulfillmentId.ToString(),
                new { entitlementId = c.Fulfillment.EntitlementId.Value, revokedAt = revocation.RevokedAt });
            count++;
        }

        if (count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return count;
    }
}
