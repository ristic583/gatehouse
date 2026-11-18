using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.LiveBuilding;

/// <summary>Current door states: <see cref="IDoorStateStore"/> first, misses rebuilt from the last 24 h of stored events (starting at
/// Locked).</summary>
public sealed class DoorStateReader(ITenantDb db, IDoorStateStore states, TimeProvider time)
{
    public static readonly TimeSpan RebuildWindow = TimeSpan.FromHours(24);

    /// <summary>Doors without a cached state and without events in the window are absent from the result.</summary>
    public async Task<Dictionary<DoorId, DoorStateDto>> GetAsync(IReadOnlyCollection<DoorId> doorIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doorIds);
        var current = (await states.GetManyAsync(doorIds, ct)).ToDictionary(kv => kv.Key, kv => kv.Value);
        var missing = doorIds.Where(id => !current.ContainsKey(id)).ToList();
        if (missing.Count == 0)
        {
            return current;
        }

        var since = time.GetUtcNow() - RebuildWindow;
        var history = await db.DoorEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= since && e.DoorId != null && missing.Contains(e.DoorId.Value))
            .OrderBy(e => e.OccurredAt)
            .Select(e => new { DoorId = e.DoorId!.Value, e.OccurredAt, e.Type })
            .ToListAsync(ct);
        foreach (var e in history)
        {
            var before = current.TryGetValue(e.DoorId, out var known)
                ? known
                : new DoorStateDto(e.DoorId.Value, DoorState.Locked, e.OccurredAt, e.OccurredAt);
            var after = DoorStateMachine.Apply(before.State, e.Type);
            current[e.DoorId] = new DoorStateDto(e.DoorId.Value, after, after == before.State ? before.Since : e.OccurredAt, e.OccurredAt);
        }

        return current;
    }
}
