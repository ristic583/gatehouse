import { describe, expect, it } from "vitest";
import { ACCESS_STATE_META, ACCESS_STATES, reasonText } from "./access-state";

describe("access-state", () => {
  it("every state has a label and an icon", () => {
    for (const state of ACCESS_STATES) {
      expect(ACCESS_STATE_META[state].label.length).toBeGreaterThan(0);
      expect(ACCESS_STATE_META[state].icon).toBeDefined();
      expect(ACCESS_STATE_META[state].colorVar).toMatch(/^--gh-state-/);
    }
  });

  it("labels are fixed strings", () => {
    expect(ACCESS_STATES.map((s) => ACCESS_STATE_META[s].label)).toEqual(["Has access", "No access", "Pending", "Unknown"]);
    expect(ACCESS_STATE_META.Pending.hatched).toBe(true);
    expect(reasonText("AwaitingFulfillment", null)).toBe("approved, not yet applied");
    expect(reasonText("AreaNotMapped", null)).toBe("not connected to a PACS");
  });
});
