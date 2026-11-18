import { DoorClosed, DoorOpen, Lock, LockOpen, ShieldAlert, Siren, Timer, CalendarClock, type LucideIcon } from "lucide-react";
import type { AlarmKind, DoorEventType, DoorState } from "@/lib/api/types";

export interface DoorStateMeta {
  label: string;
  icon: LucideIcon;
  colorVar: string;
  /** True for states that indicate a security problem (pulse/highlight in the UI). */
  alarm: boolean;
}

/** Fixed door-state semantics (ADR-007): colours come from `--gh-door-*`, never theme tokens. */
export const DOOR_STATE_META: Record<DoorState, DoorStateMeta> = {
  Locked: { label: "Locked", icon: Lock, colorVar: "--gh-door-locked", alarm: false },
  Unlocked: { label: "Unlocked", icon: LockOpen, colorVar: "--gh-door-unlocked", alarm: false },
  Open: { label: "Open", icon: DoorOpen, colorVar: "--gh-door-open", alarm: false },
  Closed: { label: "Closed", icon: DoorClosed, colorVar: "--gh-door-closed", alarm: false },
  HeldOpen: { label: "Held open", icon: Timer, colorVar: "--gh-door-held-open", alarm: true },
  ForcedOpen: { label: "Forced open", icon: Siren, colorVar: "--gh-door-forced-open", alarm: true },
  Lockdown: { label: "Lockdown", icon: ShieldAlert, colorVar: "--gh-door-lockdown", alarm: false },
  ScheduledUnlocked: {
    label: "Scheduled unlock",
    icon: CalendarClock,
    colorVar: "--gh-door-scheduled-unlocked",
    alarm: false,
  },
};

export const DOOR_STATES: readonly DoorState[] = [
  "Locked",
  "Unlocked",
  "Open",
  "Closed",
  "HeldOpen",
  "ForcedOpen",
  "Lockdown",
  "ScheduledUnlocked",
];

export const DOOR_EVENT_LABELS: Record<DoorEventType, string> = {
  AccessGranted: "Granted",
  AccessDenied: "Denied",
  DoorOpened: "Opened",
  DoorClosed: "Closed",
  DoorHeldOpen: "Held open",
  DoorForced: "Forced",
  LockdownStarted: "Lockdown started",
  LockdownCleared: "Lockdown cleared",
  ScheduledUnlockStarted: "Scheduled unlock",
  ScheduledUnlockEnded: "Scheduled unlock ended",
  DoorUnlocked: "Unlocked",
  DoorRelocked: "Relocked",
};

export const DOOR_EVENT_TYPES = Object.keys(DOOR_EVENT_LABELS) as DoorEventType[];

/** Colour var for an event row in the feed (denied/forced stand out). */
export function eventColorVar(type: DoorEventType): string {
  switch (type) {
    case "AccessGranted":
      return "--gh-door-unlocked";
    case "AccessDenied":
    case "DoorForced":
      return "--gh-door-forced-open";
    case "DoorHeldOpen":
      return "--gh-door-held-open";
    case "LockdownStarted":
      return "--gh-door-lockdown";
    default:
      return "--gh-door-closed";
  }
}

export interface AlarmKindMeta {
  label: string;
  icon: LucideIcon;
  colorVar: string;
}

/** Alarm kind labels and icons; `UnauthorizedAccess` is the live drift alarm (domain-model.md §2.7). */
export const ALARM_KIND_META: Record<AlarmKind, AlarmKindMeta> = {
  Forced: { label: "Forced open", icon: Siren, colorVar: "--gh-door-forced-open" },
  HeldOpen: { label: "Held open", icon: Timer, colorVar: "--gh-door-held-open" },
  UnauthorizedAccess: { label: "Access without entitlement", icon: ShieldAlert, colorVar: "--gh-door-forced-open" },
};
