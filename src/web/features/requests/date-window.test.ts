import { describe, expect, it } from "vitest";
import { toRequestWindow } from "./date-window";

const now = new Date(2026, 10, 9, 14, 30);

describe("toRequestWindow", () => {
  it("today starts now", () => {
    expect(toRequestWindow("2026-11-09", "2026-11-20", now).requestedStart).toBe(now.toISOString());
  });

  it("future start is local midnight", () => {
    expect(toRequestWindow("2026-11-12", "2026-11-20", now).requestedStart).toBe(new Date(2026, 10, 12).toISOString());
  });

  it("end is exclusive next midnight", () => {
    expect(toRequestWindow("2026-11-12", "2026-11-30", now).requestedEnd).toBe(new Date(2026, 11, 1).toISOString());
  });
});
