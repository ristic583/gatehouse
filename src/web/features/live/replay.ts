import type { DoorEventType, DoorState, LiveDoorEventDto, LiveFloorDto } from "@/lib/api/types";

/** Same transitions as Gatehouse.Domain.LiveBuilding.DoorStateMachine.Apply. */
export function applyDoorEvent(current: DoorState, event: DoorEventType): DoorState {
  if (event === "LockdownStarted") {
    return "Lockdown";
  }
  if (current === "Lockdown") {
    return event === "LockdownCleared" ? "Locked" : "Lockdown";
  }
  switch (current) {
    case "Locked":
      if (event === "AccessGranted" || event === "DoorUnlocked") return "Unlocked";
      if (event === "DoorOpened" || event === "DoorForced") return "ForcedOpen";
      if (event === "ScheduledUnlockStarted") return "ScheduledUnlocked";
      return current;
    case "Unlocked":
      if (event === "DoorOpened") return "Open";
      if (event === "DoorRelocked") return "Locked";
      return current;
    case "Open":
      if (event === "DoorClosed") return "Closed";
      if (event === "DoorHeldOpen") return "HeldOpen";
      return current;
    case "HeldOpen":
    case "ForcedOpen":
      return event === "DoorClosed" ? "Closed" : current;
    case "Closed":
      return event === "DoorRelocked" ? "Locked" : current;
    case "ScheduledUnlocked":
      return event === "ScheduledUnlockEnded" ? "Locked" : current;
    default:
      return current;
  }
}

/**
 * The floor as it was at `at`: door states rebuilt from Locked with the events up to `at` (oldest first), recent events limited to
 * those before `at`. Door states before the window start are unknown, so every door starts Locked (labelled in the UI).
 */
export function floorAt(floor: LiveFloorDto, events: readonly LiveDoorEventDto[], at: number): LiveFloorDto {
  const states = new Map<string, { state: DoorState; since: string | null }>(
    floor.doors.map((d) => [d.doorId, { state: "Locked" as DoorState, since: null }]),
  );
  const past = events.filter((e) => Date.parse(e.occurredAt) <= at);
  for (const e of past) {
    const known = e.doorId === null ? undefined : states.get(e.doorId);
    if (known !== undefined && e.doorId !== null) {
      const next = applyDoorEvent(known.state, e.type);
      states.set(e.doorId, { state: next, since: next === known.state ? known.since : e.occurredAt });
    }
  }
  return {
    ...floor,
    doors: floor.doors.map((d) => ({
      ...d,
      state: states.get(d.doorId)?.state ?? "Locked",
      stateSince: states.get(d.doorId)?.since ?? null,
    })),
    openAlarms: [],
    recentEvents: [...past].reverse().slice(0, 50),
  };
}
