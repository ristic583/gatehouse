import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { LiveDoorEventDto, LiveFloorDto } from "@/lib/api/types";
import { applyDoorEvent, floorAt } from "../replay";
import { ReplayScrubber } from "./replay-scrubber";

const fetchMock = vi.fn<typeof fetch>();

const floor: LiveFloorDto = {
  floorId: "f-1",
  siteId: "s-1",
  siteName: "HQ",
  floorName: "Ground",
  planWidth: 400,
  planHeight: 300,
  areas: [],
  doors: [
    {
      doorId: "d-1",
      areaId: "a-1",
      name: "Lab door",
      position: { x: 10, y: 10 },
      pacsDoorRef: "D-1",
      connectorManaged: true,
      state: "Open",
      stateSince: null,
      readers: [],
      canUnlock: false,
      canLockdown: false,
    },
  ],
  openAlarms: [],
  recentEvents: [],
};

function event(minutesAgo: number, type: LiveDoorEventDto["type"]): LiveDoorEventDto {
  return {
    eventId: `e-${minutesAgo}-${type}`,
    occurredAt: new Date(Date.now() - minutesAgo * 60_000).toISOString(),
    type,
    doorId: "d-1",
    doorName: "Lab door",
    areaId: "a-1",
    areaName: "Lab",
    readerName: null,
    personId: null,
    personDisplayName: null,
    direction: null,
  };
}

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("replay", () => {
  it("mirrors the domain door state machine", () => {
    expect(applyDoorEvent("Locked", "AccessGranted")).toBe("Unlocked");
    expect(applyDoorEvent("Locked", "DoorForced")).toBe("ForcedOpen");
    expect(applyDoorEvent("Lockdown", "AccessGranted")).toBe("Lockdown");
    expect(applyDoorEvent("Open", "DoorHeldOpen")).toBe("HeldOpen");
  });

  it("rebuilds the floor at an earlier moment", () => {
    const events = [event(120, "AccessGranted"), event(119, "DoorOpened"), event(10, "DoorClosed")];

    expect(floorAt(floor, events, Date.now() - 60 * 60_000).doors[0]?.state).toBe("Open");
    expect(floorAt(floor, events, Date.now()).doors[0]?.state).toBe("Closed");
    expect(floorAt(floor, events, Date.now() - 180 * 60_000).doors[0]?.state).toBe("Locked");
  });

  it("loads the last 24 h and moves with the slider", async () => {
    fetchMock.mockImplementation(() =>
      Promise.resolve(new Response(JSON.stringify([event(120, "AccessGranted"), event(119, "DoorOpened")]), { status: 200 })),
    );
    render(
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <ReplayScrubber floor={floor} />
      </QueryClientProvider>,
    );

    expect(await screen.findByTestId("live-floor-plan")).toBeInTheDocument();
    expect(String(fetchMock.mock.calls[0]![0])).toContain("/v1/live/floors/f-1/events?from=");
    fireEvent.change(screen.getByLabelText("Replay time"), { target: { value: "0" } });
    expect(screen.getByTestId("replay-time")).toBeInTheDocument();
  });
});
