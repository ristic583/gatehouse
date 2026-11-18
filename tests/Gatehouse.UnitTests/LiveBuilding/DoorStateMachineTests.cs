using Gatehouse.Domain.LiveBuilding;

namespace Gatehouse.UnitTests.LiveBuilding;

public sealed class DoorStateMachineTests
{
    [Theory]
    [InlineData(DoorState.Locked, DoorEventType.AccessGranted, DoorState.Unlocked)]
    [InlineData(DoorState.Locked, DoorEventType.DoorUnlocked, DoorState.Unlocked)]
    [InlineData(DoorState.Locked, DoorEventType.AccessDenied, DoorState.Locked)]
    [InlineData(DoorState.Unlocked, DoorEventType.DoorOpened, DoorState.Open)]
    [InlineData(DoorState.Unlocked, DoorEventType.DoorRelocked, DoorState.Locked)]
    [InlineData(DoorState.Open, DoorEventType.DoorClosed, DoorState.Closed)]
    [InlineData(DoorState.Open, DoorEventType.DoorHeldOpen, DoorState.HeldOpen)]
    [InlineData(DoorState.HeldOpen, DoorEventType.DoorClosed, DoorState.Closed)]
    [InlineData(DoorState.Closed, DoorEventType.DoorRelocked, DoorState.Locked)]
    [InlineData(DoorState.Locked, DoorEventType.DoorOpened, DoorState.ForcedOpen)]
    [InlineData(DoorState.Locked, DoorEventType.DoorForced, DoorState.ForcedOpen)]
    [InlineData(DoorState.ForcedOpen, DoorEventType.DoorClosed, DoorState.Closed)]
    [InlineData(DoorState.Open, DoorEventType.LockdownStarted, DoorState.Lockdown)]
    [InlineData(DoorState.Locked, DoorEventType.LockdownStarted, DoorState.Lockdown)]
    [InlineData(DoorState.Lockdown, DoorEventType.LockdownCleared, DoorState.Locked)]
    [InlineData(DoorState.Locked, DoorEventType.ScheduledUnlockStarted, DoorState.ScheduledUnlocked)]
    [InlineData(DoorState.ScheduledUnlocked, DoorEventType.ScheduledUnlockEnded, DoorState.Locked)]
    public void Apply_follows_contract_table(DoorState from, DoorEventType evt, DoorState expected) =>
        Assert.Equal(expected, DoorStateMachine.Apply(from, evt));

    [Fact]
    public void Lockdown_ignores_everything_but_clear()
    {
        foreach (var evt in Enum.GetValues<DoorEventType>().Where(e => e is not DoorEventType.LockdownCleared))
        {
            Assert.Equal(DoorState.Lockdown, DoorStateMachine.Apply(DoorState.Lockdown, evt));
        }
    }

    [Fact]
    public void Unknown_pair_keeps_state()
    {
        Assert.Equal(DoorState.Open, DoorStateMachine.Apply(DoorState.Open, DoorEventType.AccessGranted));
        Assert.Equal(DoorState.Closed, DoorStateMachine.Apply(DoorState.Closed, DoorEventType.DoorOpened));
        Assert.Equal(DoorState.Locked, DoorStateMachine.Apply(DoorState.Locked, DoorEventType.LockdownCleared));
    }
}
