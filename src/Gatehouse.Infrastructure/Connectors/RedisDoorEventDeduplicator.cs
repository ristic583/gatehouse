using Gatehouse.Application.Connectors;
using Gatehouse.Domain.Common;
using StackExchange.Redis;

namespace Gatehouse.Infrastructure.Connectors;

public sealed class RedisDoorEventDeduplicator(IConnectionMultiplexer redis) : IDoorEventDeduplicator
{
    public Task<bool> TryClaimAsync(TenantId tenantId, Guid eventId, CancellationToken ct) =>
        redis.GetDatabase().StringSetAsync(Key(tenantId, eventId), 1, IDoorEventDeduplicator.Window, When.NotExists);

    public async Task ReleaseAsync(TenantId tenantId, IReadOnlyCollection<Guid> eventIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(eventIds);
        if (eventIds.Count > 0)
        {
            await redis.GetDatabase().KeyDeleteAsync(eventIds.Select(id => Key(tenantId, id)).ToArray());
        }
    }

    private static RedisKey Key(TenantId tenantId, Guid eventId) => $"tenant:{tenantId.Value}:door-event:{eventId}";
}
