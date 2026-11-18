import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { LiveFloorDto } from "@/lib/api/types";
import { LiveFloorPlan } from "./live-floor-plan";

function floor(state: LiveFloorDto["doors"][number]["state"]): LiveFloorDto {
  return {
    floorId: "f-1",
    siteId: "s-1",
    siteName: "HQ",
    floorName: "Ground",
    planWidth: 400,
    planHeight: 300,
    areas: [
      {
        areaId: "a-1",
        name: "Server Room B",
        code: "SRV-B",
        polygon: [
          { x: 0, y: 0 },
          { x: 100, y: 0 },
          { x: 100, y: 100 },
          { x: 0, y: 100 },
        ],
      },
    ],
    doors: [
      {
        doorId: "d-1",
        areaId: "a-1",
        name: "Server door",
        position: { x: 50, y: 100 },
        pacsDoorRef: "D-SRV-B",
        connectorManaged: true,
        state,
        stateSince: "2026-11-10T08:00:00Z",
        readers: [{ readerId: "r-1", name: "R-2.04", position: { x: 40, y: 110 } }],
        canUnlock: true,
        canLockdown: true,
      },
    ],
    openAlarms: [],
    recentEvents: [],
  };
}

describe("LiveFloorPlan", () => {
  it("door shows state label", () => {
    render(<LiveFloorPlan floor={floor("Locked")} />);
    const door = screen.getByTestId("door-D-SRV-B");
    expect(door).toHaveAttribute("data-state", "Locked");
    expect(door).toHaveTextContent("Locked");
    expect(screen.getByText("Server Room B")).toBeInTheDocument();
    expect(screen.getAllByTestId("live-reader")).toHaveLength(1);
  });

  it("alarm states show a badge", () => {
    render(<LiveFloorPlan floor={floor("ForcedOpen")} />);
    expect(screen.getByTestId("door-D-SRV-B")).toHaveTextContent("Forced open");
    expect(screen.getByTestId("door-alarm-badge")).toBeInTheDocument();
  });
});
