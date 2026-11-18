using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Application.LiveBuilding.ListAlarms;

internal sealed class AlarmRow
{
    public AlarmId AlarmId { get; init; }

    public DoorId DoorId { get; init; }

    public string DoorName { get; init; } = "";

    public AreaId AreaId { get; init; }

    public string AreaName { get; init; } = "";

    public SiteId SiteId { get; init; }

    public AlarmKind Kind { get; init; }

    public DateTimeOffset RaisedAt { get; init; }

    public Guid DoorEventId { get; init; }

    public DateTimeOffset? AcknowledgedAt { get; init; }

    public string? AcknowledgedByName { get; init; }

    public string? Comment { get; init; }
}
