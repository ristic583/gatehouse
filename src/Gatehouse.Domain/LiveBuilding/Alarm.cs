using Gatehouse.Domain.Common;
using Gatehouse.Domain.LiveBuilding.Events;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Domain.LiveBuilding;

public sealed class Alarm : AggregateRoot<AlarmId>
{
    public const int MaxCommentLength = 500;

    private Alarm(AlarmId id, DoorId doorId, AlarmKind kind, Guid doorEventId, DateTimeOffset raisedAt)
        : base(id)
    {
        DoorId = doorId;
        Kind = kind;
        DoorEventId = doorEventId;
        RaisedAt = raisedAt;
    }

    // EF Core
    private Alarm()
        : base(default)
    {
    }

    public DoorId DoorId { get; private set; }

    public AlarmKind Kind { get; private set; }

    public DateTimeOffset RaisedAt { get; private set; }

    public Guid DoorEventId { get; private set; }

    public PersonId? AcknowledgedById { get; private set; }

    public DateTimeOffset? AcknowledgedAt { get; private set; }

    public string? Comment { get; private set; }

    public bool IsOpen => AcknowledgedAt is null;

    public static Alarm Raise(DoorId doorId, AlarmKind kind, Guid doorEventId, DateTimeOffset now)
    {
        var alarm = new Alarm(AlarmId.New(), doorId, kind, doorEventId, now);
        alarm.Raise(new AlarmRaised(alarm.Id, doorId, kind, doorEventId, now));
        return alarm;
    }

    /// <summary>Live drift (domain-model.md §2.7): an access granted to a person without an authorized entitlement.</summary>
    public static Alarm RaiseUnauthorizedAccess(DoorId doorId, AreaId areaId, PersonId? personId, Guid doorEventId, DateTimeOffset now)
    {
        var alarm = Raise(doorId, AlarmKind.UnauthorizedAccess, doorEventId, now);
        alarm.Raise(new LiveDriftAlertRaised(alarm.Id, personId, areaId, doorId, doorEventId, now));
        return alarm;
    }

    /// <summary>The alarm an event raises given the door state before it, or null.</summary>
    public static AlarmKind? KindFor(DoorEventType type, DoorState before) => type switch
    {
        DoorEventType.DoorForced => AlarmKind.Forced,
        DoorEventType.DoorOpened when before == DoorState.Locked => AlarmKind.Forced,
        DoorEventType.DoorHeldOpen => AlarmKind.HeldOpen,
        _ => null,
    };

    public Result Acknowledge(PersonId by, string? comment, DateTimeOffset now)
    {
        var trimmed = comment?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxCommentLength)
        {
            return Error.Validation("alarm.comment_required", "A comment of 1-500 characters is required to acknowledge an alarm.");
        }

        if (!IsOpen)
        {
            return Error.Conflict("alarm.already_acknowledged", "The alarm is already acknowledged.");
        }

        AcknowledgedById = by;
        AcknowledgedAt = now;
        Comment = trimmed;
        Raise(new AlarmAcknowledged(Id, by, trimmed, now));
        return Result.Success();
    }
}
