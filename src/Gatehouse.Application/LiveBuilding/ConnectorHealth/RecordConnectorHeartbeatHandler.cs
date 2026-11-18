using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.LiveBuilding.ConnectorHealth;

/// <summary>Caches the heartbeat and publishes a <see cref="LiveUpdateKind.ConnectorHealth"/> update to every site of the
/// instance.</summary>
public sealed class RecordConnectorHeartbeatHandler(
    ITenantDb db, IConnectorHealthCache cache, ILiveUpdatePublisher publisher, TimeProvider time)
{
    public async Task<Result<ConnectorHealthDto>> HandleAsync(RecordConnectorHeartbeatCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var instance = await db.PacsInstances.AsNoTracking().FirstOrDefaultAsync(p => p.Id == command.PacsInstanceId, ct);
        if (instance is null)
        {
            return Error.NotFound("pacs_instance.not_found", "PACS instance not found.");
        }

        var cached = await cache.GetManyAsync([command.PacsInstanceId], ct);
        if (!cached.TryGetValue(command.PacsInstanceId, out var previous) || previous.ReceivedAt <= command.ReceivedAt)
        {
            await cache.SetAsync(
                command.PacsInstanceId,
                new ConnectorHeartbeatSnapshot(command.AgentVersion, command.QueueDepth, command.PacsReachable, command.ReceivedAt), ct);
        }

        var health = await ConnectorHealthProjection.BuildAsync(db, cache, [instance], time.GetUtcNow(), ct);
        await ConnectorHealthProjection.PublishAsync(publisher, [instance], health, ct);
        return health[0];
    }
}
