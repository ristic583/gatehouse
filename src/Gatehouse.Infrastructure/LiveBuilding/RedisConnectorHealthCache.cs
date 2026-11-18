using System.Text.Json;
using Gatehouse.Application.LiveBuilding.ConnectorHealth;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Fulfillment;
using StackExchange.Redis;

namespace Gatehouse.Infrastructure.LiveBuilding;

public sealed class RedisConnectorHealthCache(IConnectionMultiplexer redis, ITenantContext tenant) : IConnectorHealthCache
{
    public Task SetAsync(PacsInstanceId pacsInstanceId, ConnectorHeartbeatSnapshot snapshot, CancellationToken ct) =>
        redis.GetDatabase()
            .StringSetAsync(Key(tenant.TenantId.Value, pacsInstanceId), JsonSerializer.Serialize(snapshot, LiveJson.Options));

    public async Task<IReadOnlyDictionary<PacsInstanceId, ConnectorHeartbeatSnapshot>> GetManyAsync(
        IReadOnlyCollection<PacsInstanceId> pacsInstanceIds, CancellationToken ct)
    {
        var result = new Dictionary<PacsInstanceId, ConnectorHeartbeatSnapshot>();
        if (pacsInstanceIds.Count == 0)
        {
            return result;
        }

        var ids = pacsInstanceIds.ToArray();
        var values = await redis.GetDatabase().StringGetAsync(ids.Select(id => (RedisKey)Key(tenant.TenantId.Value, id)).ToArray());
        for (var i = 0; i < ids.Length; i++)
        {
            if (values[i].HasValue
                && JsonSerializer.Deserialize<ConnectorHeartbeatSnapshot>(values[i].ToString(), LiveJson.Options) is { } snapshot)
            {
                result[ids[i]] = snapshot;
            }
        }

        return result;
    }

    internal static string Key(string tenantId, PacsInstanceId pacsInstanceId) =>
        $"tenant:{tenantId}:connector:{pacsInstanceId.Value}:health";
}
