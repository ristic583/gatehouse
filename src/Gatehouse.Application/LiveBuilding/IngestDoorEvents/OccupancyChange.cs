using Gatehouse.Domain.Sites;

namespace Gatehouse.Application.LiveBuilding.IngestDoorEvents;

/// <summary>
/// An occupancy adjustment of one granted In/Out event, applied after commit and at most once per event id (GH-LIVE-05). The presence
/// key identifies the person (<c>person:{id}</c>, else <c>card:{hash}</c>, else null) so a repeated In counts once (lab #21).
/// </summary>
public sealed record OccupancyChange(Guid EventId, SiteId SiteId, AreaId AreaId, DateOnly LocalDate, string? PresenceKey, int Delta)
{
    public static string? PresenceKeyOf(Domain.People.PersonId? personId, string? cardNumberHash) =>
        personId is { } p ? $"person:{p.Value}" : string.IsNullOrEmpty(cardNumberHash) ? null : $"card:{cardNumberHash}";
}
