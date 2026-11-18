using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.LiveBuilding.ConnectorHealth;

/// <summary>Marks stale connector-mode instances offline (outbox <c>ConnectorWentOffline</c> once) and publishes their health. Returns how
/// many went offline.</summary>
public sealed class SweepOfflineConnectorsHandler(ITenantDb db, IConnectorHealthCache cache, ILiveUpdatePublisher publisher)
{
    public async Task<int> HandleAsync(SweepOfflineConnectorsCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var instances = await db.PacsInstances
            .Where(p => p.FulfillmentMode == FulfillmentMode.Connector && p.OfflineNotifiedAt == null && p.LastHeartbeatAt != null)
            .ToListAsync(ct);
        var offline = instances.Where(i => i.MarkOfflineIfStale(command.Now)).ToList();
        if (offline.Count == 0)
        {
            return 0;
        }

        await db.SaveChangesAsync(ct);
        var health = await ConnectorHealthProjection.BuildAsync(db, cache, offline, command.Now, ct);
        await ConnectorHealthProjection.PublishAsync(publisher, offline, health, ct);
        return offline.Count;
    }
}
