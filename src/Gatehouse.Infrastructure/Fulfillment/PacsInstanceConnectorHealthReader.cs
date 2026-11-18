using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Fulfillment;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Infrastructure.Fulfillment;

/// <summary>Health from PACS instance heartbeats; Manual instances have no connector and count as online.</summary>
public sealed class PacsInstanceConnectorHealthReader(ITenantDb db, TimeProvider time) : IConnectorHealthReader
{
    public async Task<IReadOnlyDictionary<PacsInstanceId, ConnectorHealth>> GetAsync(
        IReadOnlyCollection<PacsInstanceId> instanceIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(instanceIds);
        var ids = instanceIds.Distinct().ToList();
        var rows = await db.PacsInstances.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.FulfillmentMode, p.LastHeartbeatAt })
            .ToListAsync(ct);
        var now = time.GetUtcNow();
        return rows.ToDictionary(
            r => r.Id,
            r => new ConnectorHealth(
                r.FulfillmentMode == FulfillmentMode.Manual
                    || (r.LastHeartbeatAt is { } last && now - last <= PacsInstance.OnlineThreshold),
                r.LastHeartbeatAt));
    }
}
