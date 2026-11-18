import { describe, expect, it } from "vitest";
import { DOOR_EVENT_LABELS, DOOR_EVENT_TYPES, DOOR_STATE_META, DOOR_STATES } from "./door-state";

describe("door-state", () => {
  it("every DoorState has a label, an icon and a --gh-door var", () => {
    expect(DOOR_STATES).toHaveLength(Object.keys(DOOR_STATE_META).length);
    for (const state of DOOR_STATES) {
      expect(DOOR_STATE_META[state].label.length).toBeGreaterThan(0);
      expect(DOOR_STATE_META[state].icon).toBeDefined();
      expect(DOOR_STATE_META[state].colorVar).toMatch(/^--gh-door-/);
    }
  });

  it("event types have fixed labels", () => {
    expect(DOOR_EVENT_LABELS.AccessGranted).toBe("Granted");
    expect(DOOR_EVENT_LABELS.AccessDenied).toBe("Denied");
    for (const type of DOOR_EVENT_TYPES) {
      expect(DOOR_EVENT_LABELS[type].length).toBeGreaterThan(0);
    }
  });

  it("forced and held-open are alarm states", () => {
    expect(DOOR_STATE_META.ForcedOpen.alarm).toBe(true);
    expect(DOOR_STATE_META.HeldOpen.alarm).toBe(true);
    expect(DOOR_STATE_META.Locked.alarm).toBe(false);
  });
});
