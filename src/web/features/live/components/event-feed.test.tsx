import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { LiveDoorEventDto } from "@/lib/api/types";
import { EventFeed, formatClock } from "./event-feed";

function event(id: string, occurredAt: string, type: LiveDoorEventDto["type"]): LiveDoorEventDto {
  return {
    eventId: id,
    occurredAt,
    type,
    doorId: "d-1",
    doorName: "Server door",
    areaId: "a-1",
    areaName: "Server Room B",
    readerName: "R-2.04",
    personId: "p-1",
    personDisplayName: "Anna Larsen",
    direction: "In",
  };
}

describe("EventFeed", () => {
  it("formats granted row", () => {
    const granted = event("e-1", "2026-11-10T08:42:13Z", "AccessGranted");
    render(<EventFeed events={[granted]} />);
    expect(screen.getByTestId("live-event")).toHaveTextContent(
      `${formatClock(granted.occurredAt)} · Anna Larsen · Server Room B · Granted · R-2.04`,
    );
    expect(formatClock(granted.occurredAt)).toMatch(/^\d{2}:\d{2}:\d{2}$/);
  });

  it("lists newest first", () => {
    render(
      <EventFeed events={[event("e-1", "2026-11-10T08:00:00Z", "AccessGranted"), event("e-2", "2026-11-10T09:00:00Z", "AccessDenied")]} />,
    );
    const rows = screen.getAllByTestId("live-event");
    expect(rows[0]).toHaveTextContent("Denied");
    expect(rows[1]).toHaveTextContent("Granted");
  });
});
