import { describe, expect, it } from "vitest";
import { availableActions } from "./actions";

describe("availableActions", () => {
  it("desired allows start confirm and fail", () => {
    expect(availableActions("Desired")).toEqual(["start", "confirm", "fail"]);
  });

  it("failed allows retry only", () => {
    expect(availableActions("Failed")).toEqual(["retry"]);
  });

  it("confirmed and superseded allow nothing", () => {
    expect(availableActions("Confirmed")).toEqual([]);
    expect(availableActions("Superseded")).toEqual([]);
  });
});
