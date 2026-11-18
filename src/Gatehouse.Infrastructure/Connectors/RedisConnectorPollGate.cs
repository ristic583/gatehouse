using Gatehouse.Application.Connectors;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using StackExchange.Redis;

namespace Gatehouse.Infrastructure.Connectors;

public sealed class RedisConnectorPollGate(IConnectionMultiplexer redis) : IConnectorPollGate
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(
        TenantId tenantId, PacsInstanceId pacsInstanceId, TimeSpan ttl, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        RedisKey key = $"tenant:{tenantId.Value}:connector:{pacsInstanceId.Value}:poll";
        RedisValue token = Guid.NewGuid().ToString("N");
        if (!await db.StringSetAsync(key, token, ttl, When.NotExists))
        {
            return null;
        }

        return new Lease(db, key, token);
    }

    private sealed class Lease(IDatabase db, RedisKey key, RedisValue token) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() =>
            await db.ScriptEvaluateAsync(
                "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end",
                [key],
                [token]);
    }
}
