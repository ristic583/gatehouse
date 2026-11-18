using Gatehouse.Domain.Sites;

namespace Gatehouse.Application.LiveBuilding;

/// <summary>
/// Approximate occupancy per area from In/Out readers (spec §6.1) and site-local date (reset at local midnight): the number of distinct
/// people inside (lab #21), plus a counter for swipes without any identity. Never below 0.
/// </summary>
public interface IOccupancyStore
{
    /// <summary>
    /// Applies one granted swipe once per <paramref name="eventId"/> and returns the count: <paramref name="presenceKey"/> (person, or
    /// card hash when the person is unknown) joins the area on In (<paramref name="delta"/> +1) and leaves it on Out (-1), so the same
    /// person swiping In twice counts once; a null key adjusts the anonymous counter. A repeated event id (redelivery, concurrent
    /// consumer) changes nothing (GH-LIVE-05).
    /// </summary>
    public Task<long> AdjustAsync(Guid eventId, AreaId areaId, DateOnly localDate, string? presenceKey, int delta, CancellationToken ct);

    public Task<IReadOnlyDictionary<AreaId, long>> GetAsync(IReadOnlyCollection<AreaId> areaIds, DateOnly localDate, CancellationToken ct);
}
