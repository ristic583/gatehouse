import { act, renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { parseMapPreferences, useMapPreferences } from "./use-map-preferences";
beforeEach(() => localStorage.clear());
describe("map preferences", () => {
  it("ignores corrupt storage and unsupported modes", () => {
    expect(parseMapPreferences("broken")).toEqual({});
    expect(parseMapPreferences('{"siteId":3,"desktopMode":"invalid","mobileMode":"list"}')).toMatchObject({
      siteId: undefined,
      desktopMode: undefined,
      mobileMode: "list",
    });
  });
  it("remembers floors and modes separately by tenant, user, and device class", () => {
    const hook = renderHook(({ tenant, user }) => useMapPreferences(tenant, user), { initialProps: { tenant: "t1", user: "u1" } });
    act(() => hook.result.current.save({ siteId: "s", floorId: "f", desktopMode: "spatial", mobileMode: "list" }));
    expect(hook.result.current.preferences.floorId).toBe("f");
    hook.rerender({ tenant: "t2", user: "u1" });
    expect(hook.result.current.preferences).toEqual({});
    hook.rerender({ tenant: "t1", user: "u2" });
    expect(hook.result.current.preferences).toEqual({});
    hook.rerender({ tenant: "t1", user: "u1" });
    expect(hook.result.current.preferences).toMatchObject({ floorId: "f", desktopMode: "spatial", mobileMode: "list" });
  });
  it("remains usable when storage is denied", () => {
    const spy = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("denied");
    });
    const hook = renderHook(() => useMapPreferences("t", "u"));
    expect(() => act(() => hook.result.current.save({ floorId: "f" }))).not.toThrow();
    spy.mockRestore();
  });
});
