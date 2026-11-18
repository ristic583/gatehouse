namespace Gatehouse.Domain.LiveBuilding;

/// <summary>Door state transitions (domain-model.md §2.6). Unknown pairs keep the current state.</summary>
public static class DoorStateMachine
{
    public static DoorState Apply(DoorState current, DoorEventType evt)
    {
        if (evt == DoorEventType.LockdownStarted)
        {
            return DoorState.Lockdown;
        }

        if (current == DoorState.Lockdown)
        {
            return evt == DoorEventType.LockdownCleared ? DoorState.Locked : DoorState.Lockdown;
        }

        return (current, evt) switch
        {
            (DoorState.Locked, DoorEventType.AccessGranted or DoorEventType.DoorUnlocked) => DoorState.Unlocked,
            (DoorState.Locked, DoorEventType.AccessDenied) => DoorState.Locked,
            (DoorState.Unlocked, DoorEventType.DoorOpened) => DoorState.Open,
            (DoorState.Unlocked, DoorEventType.DoorRelocked) => DoorState.Locked,
            (DoorState.Open, DoorEventType.DoorClosed) => DoorState.Closed,
            (DoorState.Open, DoorEventType.DoorHeldOpen) => DoorState.HeldOpen,
            (DoorState.HeldOpen, DoorEventType.DoorClosed) => DoorState.Closed,
            (DoorState.Closed, DoorEventType.DoorRelocked) => DoorState.Locked,
            (DoorState.Locked, DoorEventType.DoorOpened or DoorEventType.DoorForced) => DoorState.ForcedOpen,
            (DoorState.ForcedOpen, DoorEventType.DoorClosed) => DoorState.Closed,
            (DoorState.Locked, DoorEventType.ScheduledUnlockStarted) => DoorState.ScheduledUnlocked,
            (DoorState.ScheduledUnlocked, DoorEventType.ScheduledUnlockEnded) => DoorState.Locked,
            _ => current,
        };
    }
}
