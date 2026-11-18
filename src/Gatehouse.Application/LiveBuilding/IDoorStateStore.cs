using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Application.LiveBuilding;

/// <summary>Current door state cache of the current tenant (Redis in production).</summary>
public interface IDoorStateStore
{
    /// <summary>Doors without a cached state are absent from the result.</summary>
    public Task<IReadOnlyDictionary<DoorId, DoorStateDto>> GetManyAsync(IReadOnlyCollection<DoorId> doorIds, CancellationToken ct);

    /// <summary>Writes the state unless the cached one has a later <c>LastEventAt</c> (atomic compare: GH-LIVE-04); false when
    /// skipped.</summary>
    public Task<bool> SetAsync(DoorStateDto state, CancellationToken ct);
}
