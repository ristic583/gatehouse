namespace Gatehouse.Connector.Contracts.Events;

public enum DoorEventType
{
    AccessGranted, AccessDenied, DoorOpened, DoorClosed, DoorHeldOpen, DoorForced,
    LockdownStarted, LockdownCleared, ScheduledUnlockStarted, ScheduledUnlockEnded, DoorUnlocked, DoorRelocked,
}
