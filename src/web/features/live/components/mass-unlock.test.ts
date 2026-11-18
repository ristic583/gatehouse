import { describe, expect, it } from "vitest";
import { isMassUnlock } from "./live-view";

describe("isMassUnlock", () => {
  it("flags a floor whose doors are all unlocked", () => {
    expect(isMassUnlock([{ state: "Unlocked" }, { state: "Open" }, { state: "Unlocked" }])).toBe(true);
  });

  it("ignores a single door and partly unlocked floors", () => {
    expect(isMassUnlock([{ state: "Unlocked" }])).toBe(false);
    expect(isMassUnlock([{ state: "Unlocked" }, { state: "Locked" }])).toBe(false);
  });
});
