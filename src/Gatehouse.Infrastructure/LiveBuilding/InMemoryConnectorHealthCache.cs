using System.Collections.Concurrent;
using Gatehouse.Application.LiveBuilding.ConnectorHealth;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Infrastructure.LiveBuilding;

/// <summary>Used when Redis is not configured (tests); <see cref="Store"/> is the process-wide storage (keys as in Redis).</summary>
public sealed class InMemoryConnectorHealthCache : IConnectorHealthCache
{
    private readonly Store _store;
    private readonly ITenantContext _tenant;

    internal InMemoryConnectorHealthCache(Store store, ITenantContext tenant)
    {
        _store = store;
        _tenant = tenant;
    }

    public Task SetAsync(PacsInstanceId pacsInstanceId, ConnectorHeartbeatSnapshot snapshot, CancellationToken ct)
    {
        _store.Entries[RedisConnectorHealthCache.Key(_tenant.TenantId.Value, pacsInstanceId)] = snapshot;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<PacsInstanceId, ConnectorHeartbeatSnapshot>> GetManyAsync(
        IReadOnlyCollection<PacsInstanceId> pacsInstanceIds, CancellationToken ct)
    {
        var result = new Dictionary<PacsInstanceId, ConnectorHeartbeatSnapshot>();
        foreach (var id in pacsInstanceIds)
        {
            if (_store.Entries.TryGetValue(RedisConnectorHealthCache.Key(_tenant.TenantId.Value, id), out var snapshot))
            {
                result[id] = snapshot;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<PacsInstanceId, ConnectorHeartbeatSnapshot>>(result);
    }

    internal sealed class Store
    {
        public ConcurrentDictionary<string, ConnectorHeartbeatSnapshot> Entries { get; } = new(StringComparer.Ordinal);
    }
}
