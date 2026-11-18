using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Tasks.FulfillmentFollowUps;

/// <summary>Consumer of <c>FulfillmentFailed</c> (ADR-008): opens the staff task; the consumer saves.</summary>
public sealed class OpenFulfillmentFailedTaskHandler(ITenantDb db, StaffTaskService tasks)
{
    public async Task HandleAsync(OpenFulfillmentFailedTaskCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var info = await db.Fulfillments.AsNoTracking()
            .Where(f => f.Id == command.FulfillmentId)
            .Join(db.Entitlements, f => f.EntitlementId, e => e.Id, (f, e) => new { f.Status, e.AreaId })
            .Join(db.Areas, x => x.AreaId, a => a.Id, (x, a) => new { x.Status, a.FloorId })
            .Join(db.Floors, x => x.FloorId, fl => fl.Id, (x, fl) => new { x.Status, fl.SiteId })
            .SingleOrDefaultAsync(ct);

        // A retry or confirmation may already have overtaken this event.
        if (info is null || info.Status != FulfillmentStatus.Failed)
        {
            return;
        }

        await tasks.OpenAsync(
            StaffTaskKind.FulfillmentFailed, info.SiteId, "Fulfillment", command.FulfillmentId.Value,
            AuditActor.ForSystem("fulfillment-consumer"), command.Now, ct);
    }
}
