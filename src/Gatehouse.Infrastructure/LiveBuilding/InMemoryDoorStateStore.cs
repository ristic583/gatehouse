using Gatehouse.Application.LiveBuilding;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Infrastructure.LiveBuilding;

public sealed class InMemoryDoorStateStore(InMemoryDoorStateCache cache, ITenantContext tenant) : IDoorStateStore
{
    public Task<IReadOnlyDictionary<DoorId, DoorStateDto>> GetManyAsync(IReadOnlyCollection<DoorId> doorIds, CancellationToken ct)
    {
        var result = new Dictionary<DoorId, DoorStateDto>();
        foreach (var id in doorIds)
        {
            if (cache.Entries.TryGetValue(DoorStateKeys.For(tenant.TenantId.Value, id), out var state))
            {
                result[id] = state;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<DoorId, DoorStateDto>>(result);
    }

    public Task<bool> SetAsync(DoorStateDto state, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(state);
        var key = DoorStateKeys.For(tenant.TenantId.Value, new DoorId(state.DoorId));
        while (true)
        {
            if (!cache.Entries.TryGetValue(key, out var current))
            {
                if (cache.Entries.TryAdd(key, state))
                {
                    return Task.FromResult(true);
                }

                continue;
            }

            if (current.LastEventAt > state.LastEventAt)
            {
                return Task.FromResult(false);
            }

            if (cache.Entries.TryUpdate(key, state, current))
            {
                return Task.FromResult(true);
            }
        }
    }
}
