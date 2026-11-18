using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.LiveBuilding.ConnectorHealth;

/// <summary>Builds <see cref="ConnectorHealthDto"/>s from instances, cached heartbeats and connector commands.</summary>
internal static class ConnectorHealthProjection
{
    public static async Task<IReadOnlyList<ConnectorHealthDto>> BuildAsync(
        ITenantDb db,
        IConnectorHealthCache cache,
        IReadOnlyList<PacsInstance> instances,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var ids = instances.Where(i => i.FulfillmentMode == FulfillmentMode.Connector).Select(i => i.Id).ToList();
        var snapshots = await cache.GetManyAsync(ids, ct);
        var queued = (await db.ConnectorCommands.AsNoTracking()
            .Where(c => ids.Contains(c.PacsInstanceId) && c.Status == ConnectorCommandStatus.Queued)
            .GroupBy(c => c.PacsInstanceId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync(ct)).ToDictionary(x => x.Id, x => x.Count);
        var latency = new Dictionary<PacsInstanceId, long>();
        foreach (var id in ids)
        {
            var last = await db.ConnectorCommands.AsNoTracking()
                .Where(c => c.PacsInstanceId == id && c.AckedAt != null)
                .OrderByDescending(c => c.AckedAt)
                .Select(c => new { c.CreatedAt, c.AckedAt })
                .FirstOrDefaultAsync(ct);
            if (last?.AckedAt is { } acked)
            {
                latency[id] = (long)(acked - last.CreatedAt).TotalMilliseconds;
            }
        }

        return instances.Select(i =>
        {
            if (i.FulfillmentMode == FulfillmentMode.Manual)
            {
                return new ConnectorHealthDto(
                    i.Id.Value, i.Name, i.Vendor.ToString(), i.FulfillmentMode, true, null, null, null, 0, null, null);
            }

            var snapshot = snapshots.GetValueOrDefault(i.Id);
            return new ConnectorHealthDto(
                i.Id.Value,
                i.Name,
                i.Vendor.ToString(),
                i.FulfillmentMode,
                i.IsOnline(now),
                i.LastHeartbeatAt,
                i.AgentVersion ?? snapshot?.AgentVersion,
                snapshot?.QueueDepth,
                queued.GetValueOrDefault(i.Id),
                latency.TryGetValue(i.Id, out var ms) ? ms : null,
                snapshot?.PacsReachable,
                PacsCapabilityGaps.From(i.CapabilitiesJson));
        }).ToList();
    }

    public static async Task PublishAsync(
        ILiveUpdatePublisher publisher, IReadOnlyList<PacsInstance> instances, IReadOnlyList<ConnectorHealthDto> health,
        CancellationToken ct)
    {
        foreach (var dto in health)
        {
            var instance = instances.First(i => i.Id.Value == dto.PacsInstanceId);
            foreach (var siteId in instance.SiteIds)
            {
                await publisher.PublishAsync(new LiveUpdateDto(LiveUpdateKind.ConnectorHealth, siteId.Value, Connector: dto), ct);
            }
        }
    }
}
