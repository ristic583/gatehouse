using System.Collections.Concurrent;
using Gatehouse.Application.LiveBuilding;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Infrastructure.LiveBuilding;

/// <summary>In-process occupancy (tests, no Redis): a presence set per area and day plus an anonymous counter, like Redis.</summary>
public sealed class InMemoryOccupancyStore(InMemoryOccupancyStore.Counters counters, ITenantContext tenant) : IOccupancyStore
{
    public Task<long> AdjustAsync(Guid eventId, AreaId areaId, DateOnly localDate, string? presenceKey, int delta, CancellationToken ct)
    {
        var area = counters.Areas.GetOrAdd(Key(areaId, localDate), _ => new AreaPresence());
        lock (area)
        {
            if (counters.Applied.TryAdd($"{tenant.TenantId.Value}:{eventId}", 0))
            {
                if (presenceKey is null)
                {
                    area.Anonymous = Math.Max(0, area.Anonymous + delta);
                }
                else if (delta > 0)
                {
                    area.People.Add(presenceKey);
                }
                else
                {
                    area.People.Remove(presenceKey);
                }
            }

            return Task.FromResult(area.Count);
        }
    }

    public Task<IReadOnlyDictionary<AreaId, long>> GetAsync(IReadOnlyCollection<AreaId> areaIds, DateOnly localDate, CancellationToken ct)
    {
        var result = new Dictionary<AreaId, long>();
        foreach (var areaId in areaIds)
        {
            if (counters.Areas.TryGetValue(Key(areaId, localDate), out var area))
            {
                lock (area)
                {
                    result[areaId] = area.Count;
                }
            }
        }

        return Task.FromResult<IReadOnlyDictionary<AreaId, long>>(result);
    }

    private string Key(AreaId areaId, DateOnly date) => $"{tenant.TenantId.Value}:{date:yyyyMMdd}:{areaId.Value}";

    /// <summary>Singleton storage shared by the scoped stores.</summary>
    public sealed class Counters
    {
        public ConcurrentDictionary<string, AreaPresence> Areas { get; } = new(StringComparer.Ordinal);

        /// <summary>Event ids already counted (tests only; Redis keeps them 48 h).</summary>
        public ConcurrentDictionary<string, byte> Applied { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>People inside one area on one day, plus swipes without identity.</summary>
    public sealed class AreaPresence
    {
        public HashSet<string> People { get; } = new(StringComparer.Ordinal);

        public long Anonymous { get; set; }

        public long Count => People.Count + Anonymous;
    }
}
