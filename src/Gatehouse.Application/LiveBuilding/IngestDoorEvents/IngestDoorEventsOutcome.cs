using Gatehouse.Application.LiveBuilding.Dtos;

namespace Gatehouse.Application.LiveBuilding.IngestDoorEvents;

/// <summary>What to push after the ingest transaction committed: occupancy changes, door states for the cache and live updates.</summary>
public sealed record IngestDoorEventsOutcome(IReadOnlyList<DoorStateDto> States, IReadOnlyList<LiveUpdateDto> Updates)
{
    public IReadOnlyList<OccupancyChange> Occupancy { get; init; } = [];

    public static IngestDoorEventsOutcome Empty { get; } = new([], []);
}
