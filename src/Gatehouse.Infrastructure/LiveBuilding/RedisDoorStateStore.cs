using System.Text.Json;
using Gatehouse.Application.LiveBuilding;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Sites;
using StackExchange.Redis;

namespace Gatehouse.Infrastructure.LiveBuilding;

/// <summary>
/// State JSON at <see cref="DoorStateKeys"/>; a companion <c>{key}:ts</c> holds its <c>LastEventAt</c> (Unix ms) so a write of an older
/// state is skipped atomically (GH-LIVE-04).
/// </summary>
public sealed class RedisDoorStateStore(IConnectionMultiplexer redis, ITenantContext tenant) : IDoorStateStore
{
    private const string SetIfNewerScript = """
        local current = redis.call('GET', KEYS[2])
        if current and tonumber(current) > tonumber(ARGV[2]) then return 0 end
        redis.call('SET', KEYS[1], ARGV[1])
        redis.call('SET', KEYS[2], ARGV[2])
        return 1
        """;

    public async Task<IReadOnlyDictionary<DoorId, DoorStateDto>> GetManyAsync(IReadOnlyCollection<DoorId> doorIds, CancellationToken ct)
    {
        var result = new Dictionary<DoorId, DoorStateDto>();
        if (doorIds.Count == 0)
        {
            return result;
        }

        var ids = doorIds.ToArray();
        var values = await redis.GetDatabase().StringGetAsync(ids.Select(Key).ToArray());
        for (var i = 0; i < ids.Length; i++)
        {
            if (values[i].HasValue && JsonSerializer.Deserialize<DoorStateDto>(values[i].ToString(), LiveJson.Options) is { } state)
            {
                result[ids[i]] = state;
            }
        }

        return result;
    }

    public async Task<bool> SetAsync(DoorStateDto state, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(state);
        var key = DoorStateKeys.For(tenant.TenantId.Value, new DoorId(state.DoorId));
        var written = await redis.GetDatabase().ScriptEvaluateAsync(SetIfNewerScript, [key, key + ":ts"],
            [JsonSerializer.Serialize(state, LiveJson.Options), state.LastEventAt.ToUnixTimeMilliseconds()]);
        return (long)written == 1;
    }

    private RedisKey Key(DoorId doorId) => DoorStateKeys.For(tenant.TenantId.Value, doorId);
}
