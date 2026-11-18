import { describe, expect, it } from "vitest";
import { addCalendarDays, formatAccessTime, validateRequestWindow } from "./request-window";
const now = new Date(2026, 10, 14, 12);
const purpose = "Maintenance for the research project";
describe("request window validation", () => {
  it("rejects missing, rolled-over, past and reversed dates", () => {
    for (const [start, end] of [
      ["", "2026-11-14"],
      ["2026-02-30", "2026-11-14"],
      ["2026-11-13", "2026-11-14"],
      ["2026-11-15", "2026-11-14"],
    ])
      expect(validateRequestWindow(start!, end!, purpose, 7, now)).not.toBeNull();
  });
  it("checks the inclusive end date against the elapsed-time policy", () => {
    expect(validateRequestWindow("2026-11-15", "2026-11-21", purpose, 7, now)).toBeNull();
    expect(validateRequestWindow("2026-11-15", "2026-11-22", purpose, 7, now)).toMatch(/maximum of 7 days/);
    expect(validateRequestWindow("2026-11-14", "2026-11-14", "         ", 7, now)).toMatch(/10 characters/);
  });
  it("handles calendar boundaries and displays a time-zone offset", () => {
    expect(addCalendarDays("2026-12-30", 6)).toBe("2027-01-05");
    expect(formatAccessTime("2026-11-14T12:00:00Z")).toMatch(/GMT|UTC/);
  });
});
