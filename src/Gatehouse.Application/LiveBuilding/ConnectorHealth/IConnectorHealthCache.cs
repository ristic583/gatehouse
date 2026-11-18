using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.LiveBuilding.ConnectorHealth;

/// <summary>Latest heartbeat per PACS instance of the current tenant.</summary>
public interface IConnectorHealthCache
{
    public Task SetAsync(PacsInstanceId pacsInstanceId, ConnectorHeartbeatSnapshot snapshot, CancellationToken ct);

    public Task<IReadOnlyDictionary<PacsInstanceId, ConnectorHeartbeatSnapshot>> GetManyAsync(
        IReadOnlyCollection<PacsInstanceId> pacsInstanceIds, CancellationToken ct);
}
