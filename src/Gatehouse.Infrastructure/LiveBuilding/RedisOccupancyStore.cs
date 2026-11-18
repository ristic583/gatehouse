using System.Globalization;
using Gatehouse.Application.LiveBuilding;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Sites;
using StackExchange.Redis;

namespace Gatehouse.Infrastructure.LiveBuilding;

/// <summary>
/// Presence set <c>tenant:{t}:presence:{yyyyMMdd}:{areaId}</c> of person (or card hash) keys, In = add, Out = remove (lab #21), plus
/// the counter <c>tenant:{t}:presence-anon:{yyyyMMdd}:{areaId}</c> for swipes without identity (clamped at 0); count = set size +
/// counter. One Lua script also sets the event marker <c>tenant:{t}:occupancy-event:{eventId}</c> (once per event: GH-LIVE-05); every
/// key lives 48 h.
/// </summary>
public sealed class RedisOccupancyStore(IConnectionMultiplexer redis, ITenantContext tenant) : IOccupancyStore
{
    private const string AdjustScript = """
        local function total()
            return redis.call('SCARD', KEYS[1]) + tonumber(redis.call('GET', KEYS[2]) or '0')
        end
        if not redis.call('SET', KEYS[3], '1', 'NX', 'EX', 172800) then
            return total()
        end
        local delta = tonumber(ARGV[2])
        if ARGV[1] == '' then
            local v = redis.call('INCRBY', KEYS[2], delta)
            if v < 0 then redis.call('SET', KEYS[2], 0) end
            redis.call('EXPIRE', KEYS[2], 172800)
        elseif delta > 0 then
            redis.call('SADD', KEYS[1], ARGV[1])
            redis.call('EXPIRE', KEYS[1], 172800)
        else
            redis.call('SREM', KEYS[1], ARGV[1])
        end
        return total()
        """;

    public async Task<long> AdjustAsync(
        Guid eventId, AreaId areaId, DateOnly localDate, string? presenceKey, int delta, CancellationToken ct)
    {
        var marker = $"tenant:{tenant.TenantId.Value}:occupancy-event:{eventId}";
        var result = await redis.GetDatabase().ScriptEvaluateAsync(
            AdjustScript, [PresenceKey(areaId, localDate), AnonymousKey(areaId, localDate), marker], [presenceKey ?? string.Empty, delta]);
        return (long)result;
    }

    public async Task<IReadOnlyDictionary<AreaId, long>> GetAsync(
        IReadOnlyCollection<AreaId> areaIds, DateOnly localDate, CancellationToken ct)
    {
        var ids = areaIds.ToArray();
        var result = new Dictionary<AreaId, long>();
        if (ids.Length == 0)
        {
            return result;
        }

        var batch = redis.GetDatabase().CreateBatch();
        var people = ids.Select(a => batch.SetLengthAsync(PresenceKey(a, localDate))).ToArray();
        var anonymous = ids.Select(a => batch.StringGetAsync(AnonymousKey(a, localDate))).ToArray();
        batch.Execute();
        await Task.WhenAll(people.Cast<Task>().Concat(anonymous));
        for (var i = 0; i < ids.Length; i++)
        {
            var anon = anonymous[i].Result;
            long count = 0;
            var hasAnon = anon.HasValue && long.TryParse(anon.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
            if (people[i].Result > 0 || hasAnon)
            {
                result[ids[i]] = people[i].Result + count;
            }
        }

        return result;
    }

    private RedisKey PresenceKey(AreaId areaId, DateOnly date) =>
        $"tenant:{tenant.TenantId.Value}:presence:{Day(date)}:{areaId.Value}";

    private RedisKey AnonymousKey(AreaId areaId, DateOnly date) =>
        $"tenant:{tenant.TenantId.Value}:presence-anon:{Day(date)}:{areaId.Value}";

    private static string Day(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
}
