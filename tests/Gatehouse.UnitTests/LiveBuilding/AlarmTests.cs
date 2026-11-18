using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.LiveBuilding.Events;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;

namespace Gatehouse.UnitTests.LiveBuilding;

public sealed class AlarmTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Acknowledge_requires_comment()
    {
        var alarm = Alarm.Raise(DoorId.New(), AlarmKind.Forced, Guid.NewGuid(), Now);
        var result = alarm.Acknowledge(PersonId.New(), "  ", Now.AddMinutes(1));
        Assert.Equal("alarm.comment_required", result.Error.Code);
        Assert.True(alarm.IsOpen);
    }

    [Fact]
    public void Acknowledge_twice_conflicts()
    {
        var alarm = Alarm.Raise(DoorId.New(), AlarmKind.HeldOpen, Guid.NewGuid(), Now);
        alarm.ClearDomainEvents();
        Assert.True(alarm.Acknowledge(PersonId.New(), "Checked on camera", Now.AddMinutes(1)).IsSuccess);
        Assert.IsType<AlarmAcknowledged>(Assert.Single(alarm.DomainEvents));
        Assert.Equal("alarm.already_acknowledged", alarm.Acknowledge(PersonId.New(), "again", Now.AddMinutes(2)).Error.Code);
    }

    [Fact]
    public void Kind_for_opened_while_locked_is_forced()
    {
        Assert.Equal(AlarmKind.Forced, Alarm.KindFor(DoorEventType.DoorOpened, DoorState.Locked));
        Assert.Equal(AlarmKind.Forced, Alarm.KindFor(DoorEventType.DoorForced, DoorState.Locked));
        Assert.Equal(AlarmKind.HeldOpen, Alarm.KindFor(DoorEventType.DoorHeldOpen, DoorState.Open));
        Assert.Null(Alarm.KindFor(DoorEventType.DoorOpened, DoorState.Unlocked));
    }

    [Fact]
    public void Unauthorized_access_raises_alarm_and_live_drift_event()
    {
        var doorId = DoorId.New();
        var areaId = AreaId.New();
        var personId = PersonId.New();
        var eventId = Guid.NewGuid();
        var alarm = Alarm.RaiseUnauthorizedAccess(doorId, areaId, personId, eventId, Now);
        Assert.Equal(AlarmKind.UnauthorizedAccess, alarm.Kind);
        Assert.True(alarm.IsOpen);
        Assert.Equal(2, alarm.DomainEvents.Count);
        var raised = Assert.IsType<AlarmRaised>(alarm.DomainEvents[0]);
        Assert.Equal(AlarmKind.UnauthorizedAccess, raised.Kind);
        var drift = Assert.IsType<LiveDriftAlertRaised>(alarm.DomainEvents[1]);
        Assert.Equal(alarm.Id, drift.AlarmId);
        Assert.Equal(personId, drift.PersonId);
        Assert.Equal(areaId, drift.AreaId);
        Assert.Equal(doorId, drift.DoorId);
        Assert.Equal(eventId, drift.DoorEventId);
    }
}
