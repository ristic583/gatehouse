import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, renderHook, waitFor } from "@testing-library/react";
import { createElement, type ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";
import type { AlarmDto, LiveFloorDto, LiveUpdateDto } from "@/lib/api/types";
import { liveKeys } from "../api";
import type { LiveConnection } from "../types";
import { INITIAL_START_RETRY_MS, useLiveSite } from "./use-live-site";

const SITE = "site-1";
const FLOOR = "floor-1";
const DOOR = "door-1";

class FakeConnection implements LiveConnection {
  handler: ((update: LiveUpdateDto) => void) | null = null;
  revokedHandler: ((siteId: string, code: string) => void) | null = null;
  reconnecting: Array<() => void> = [];
  reconnected: Array<() => void> = [];
  closed: Array<() => void> = [];
  invocations: Array<[string, string]> = [];
  started = 0;
  stopped = 0;
  failStarts = 0;

  on(method: "live" | "SubscriptionRevoked", handler: ((update: LiveUpdateDto) => void) | ((siteId: string, code: string) => void)): void {
    if (method === "live") this.handler = handler as (update: LiveUpdateDto) => void;
    else this.revokedHandler = handler as (siteId: string, code: string) => void;
  }
  onreconnecting(callback: () => void): void {
    this.reconnecting.push(callback);
  }
  onreconnected(callback: () => void): void {
    this.reconnected.push(callback);
  }
  onclose(callback: () => void): void {
    this.closed.push(callback);
  }
  start(): Promise<void> {
    this.started++;
    if (this.failStarts > 0) {
      this.failStarts--;
      return Promise.reject(new Error("API unavailable"));
    }
    return Promise.resolve();
  }
  stop(): Promise<void> {
    this.stopped++;
    this.closed.forEach((cb) => cb());
    return Promise.resolve();
  }
  invoke(method: "JoinSite", siteId: string): Promise<unknown> {
    this.invocations.push([method, siteId]);
    return Promise.resolve();
  }
  emit(update: LiveUpdateDto): void {
    this.handler?.(update);
  }
}

function floor(): LiveFloorDto {
  return {
    floorId: FLOOR,
    siteId: SITE,
    siteName: "HQ",
    floorName: "Ground",
    planWidth: 100,
    planHeight: 100,
    areas: [{ areaId: "area-1", name: "Lab", code: "CPH-LAB", polygon: [] }],
    doors: [
      {
        doorId: DOOR,
        areaId: "area-1",
        name: "Lab door",
        position: { x: 1, y: 1 },
        pacsDoorRef: "D-1",
        connectorManaged: true,
        state: "Locked",
        stateSince: null,
        readers: [],
        canUnlock: true,
        canLockdown: true,
      },
    ],
    openAlarms: [],
    recentEvents: [],
  };
}

function setup(failStarts = 0) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } });
  client.setQueryData(liveKeys.floor(FLOOR), floor());
  const connection = new FakeConnection();
  connection.failStarts = failStarts;
  const factory = () => connection;
  const wrapper = ({ children }: { children: ReactNode }) => createElement(QueryClientProvider, { client }, children);
  const hook = renderHook(() => useLiveSite(SITE, { connectionFactory: factory }), { wrapper });
  return { client, connection, hook };
}

describe("useLiveSite", () => {
  it("applies door state update to floor cache", async () => {
    const { client, connection, hook } = setup();
    await waitFor(() => expect(hook.result.current).toBe("connected"));

    act(() => {
      connection.emit({
        kind: "DoorState",
        siteId: SITE,
        doorState: { doorId: DOOR, state: "ForcedOpen", since: "2026-11-10T10:00:00Z", lastEventAt: "2026-11-10T10:00:00Z" },
        doorEvent: null,
        alarm: null,
        connector: null,
      });
    });

    const cached = client.getQueryData<LiveFloorDto>(liveKeys.floor(FLOOR));
    expect(cached?.doors[0]?.state).toBe("ForcedOpen");
    expect(cached?.doors[0]?.stateSince).toBe("2026-11-10T10:00:00Z");
  });

  it("applies occupancy update to the area", async () => {
    const { client, connection, hook } = setup();
    await waitFor(() => expect(hook.result.current).toBe("connected"));

    act(() => {
      connection.emit({
        kind: "Occupancy",
        siteId: SITE,
        doorState: null,
        doorEvent: null,
        alarm: null,
        connector: null,
        occupancy: { areaId: "area-1", count: 3 },
      });
      connection.emit({
        kind: "Occupancy",
        siteId: SITE,
        doorState: null,
        doorEvent: null,
        alarm: null,
        connector: null,
        occupancy: { areaId: "area-1", count: 2 },
      });
    });

    expect(client.getQueryData<LiveFloorDto>(liveKeys.floor(FLOOR))?.occupancy).toEqual([{ areaId: "area-1", count: 2 }]);
  });

  it("retries a failed initial start until the API is back (GH-WEB-02)", async () => {
    vi.useFakeTimers();
    try {
      const { connection, hook } = setup(2);
      await act(async () => {
        await Promise.resolve();
      });
      expect(hook.result.current).toBe("disconnected");
      expect(connection.started).toBe(1);

      await act(async () => {
        await vi.advanceTimersByTimeAsync(INITIAL_START_RETRY_MS[0]!);
      });
      expect(connection.started).toBe(2);
      expect(hook.result.current).toBe("disconnected");

      await act(async () => {
        await vi.advanceTimersByTimeAsync(INITIAL_START_RETRY_MS[1]!);
      });
      expect(connection.started).toBe(3);
      expect(hook.result.current).toBe("connected");
      expect(connection.invocations).toEqual([["JoinSite", SITE]]);
    } finally {
      vi.useRealTimers();
    }
  });

  it("stops retrying after unmount", async () => {
    vi.useFakeTimers();
    try {
      const { connection, hook } = setup(5);
      await act(async () => {
        await Promise.resolve();
      });
      hook.unmount();
      await act(async () => {
        await vi.advanceTimersByTimeAsync(60_000);
      });
      expect(connection.started).toBe(1);
    } finally {
      vi.useRealTimers();
    }
  });

  it("shows not permitted after a revoked notice and stops retrying", async () => {
    vi.useFakeTimers();
    try {
      const { connection, hook } = setup();
      await act(async () => {
        await Promise.resolve();
        await Promise.resolve();
      });
      expect(hook.result.current).toBe("connected");

      act(() => connection.revokedHandler?.(SITE, "auth.forbidden"));
      expect(hook.result.current).toBe("forbidden");
      expect(connection.stopped).toBe(1);

      act(() => connection.reconnected.forEach((cb) => cb()));
      await act(async () => {
        await vi.advanceTimersByTimeAsync(60_000);
      });
      expect(hook.result.current).toBe("forbidden");
      expect(connection.started).toBe(1);
      expect(connection.invocations).toEqual([["JoinSite", SITE]]);
    } finally {
      vi.useRealTimers();
    }
  });

  it("joins site after reconnect", async () => {
    const { connection, hook } = setup();
    await waitFor(() => expect(hook.result.current).toBe("connected"));
    expect(connection.invocations).toEqual([["JoinSite", SITE]]);

    act(() => connection.reconnecting.forEach((cb) => cb()));
    expect(hook.result.current).toBe("reconnecting");
    act(() => connection.reconnected.forEach((cb) => cb()));

    await waitFor(() => expect(hook.result.current).toBe("connected"));
    expect(connection.invocations).toEqual([
      ["JoinSite", SITE],
      ["JoinSite", SITE],
    ]);
  });

  it("removes acknowledged alarm from open alarm list", async () => {
    const { client, connection, hook } = setup();
    await waitFor(() => expect(hook.result.current).toBe("connected"));
    const alarm: AlarmDto = {
      alarmId: "a-1",
      doorId: DOOR,
      doorName: "Lab door",
      areaId: "area-1",
      areaName: "Lab",
      siteId: SITE,
      kind: "Forced",
      raisedAt: "2026-11-10T10:00:00Z",
      doorEventId: "e-1",
      acknowledgedAt: null,
      acknowledgedByName: null,
      comment: null,
    };
    client.setQueryData(liveKeys.alarms("open", SITE), [] as AlarmDto[]);
    const emit = (a: AlarmDto) =>
      act(() => connection.emit({ kind: "Alarm", siteId: SITE, alarm: a, doorEvent: null, doorState: null, connector: null }));

    emit(alarm);
    expect(client.getQueryData<AlarmDto[]>(liveKeys.alarms("open", SITE))).toHaveLength(1);
    expect(client.getQueryData<LiveFloorDto>(liveKeys.floor(FLOOR))?.openAlarms).toHaveLength(1);

    emit({ ...alarm, acknowledgedAt: "2026-11-10T10:01:00Z", acknowledgedByName: "Ana" });
    expect(client.getQueryData<AlarmDto[]>(liveKeys.alarms("open", SITE))).toHaveLength(0);
    expect(client.getQueryData<LiveFloorDto>(liveKeys.floor(FLOOR))?.openAlarms).toHaveLength(0);
  });
});
