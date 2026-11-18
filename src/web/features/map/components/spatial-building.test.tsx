import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { SpatialBuilding } from "./spatial-building";
import type { FloorAccessMapDto } from "@/lib/api/types";
const map: FloorAccessMapDto = {
  floorId: "floor",
  siteId: "site",
  floorName: "First floor",
  planWidth: 1000,
  planHeight: 600,
  areas: [
    {
      areaId: "lab",
      code: "LAB",
      name: "Lab",
      polygon: [
        { x: 10, y: 10 },
        { x: 400, y: 10 },
        { x: 400, y: 300 },
        { x: 10, y: 300 },
      ],
      securityLevelName: "High",
      state: "Pending",
      reason: "AwaitingApproval",
      cardInactive: false,
      since: null,
      doors: [],
    },
  ],
};
describe("spatial building", () => {
  it("makes the same real area and access state available to keyboard users", () => {
    const select = vi.fn();
    render(
      <SpatialBuilding
        floors={[{ floorId: "floor", name: "First floor", level: 1 }]}
        maps={[map]}
        activeFloorId="floor"
        onFloorSelect={() => {}}
        onAreaSelect={select}
      />,
    );
    const room = screen.getByRole("button", { name: "Lab: Pending" });
    expect(room).toHaveAttribute("tabindex", "0");
    fireEvent.keyDown(room, { key: "Enter" });
    expect(select).toHaveBeenCalledWith(map.areas[0]);
    expect(room.querySelector('[fill="url(#spatial-pending)"]')).toBeInTheDocument();
  });
  it("keeps unavailable floors selectable without inventing rooms", () => {
    const select = vi.fn();
    render(
      <SpatialBuilding
        floors={[{ floorId: "floor", name: "First floor", level: 1 }]}
        maps={[]}
        activeFloorId="floor"
        onFloorSelect={select}
        onAreaSelect={() => {}}
      />,
    );
    fireEvent.keyDown(screen.getByRole("button", { name: "Select floor First floor" }), { key: " " });
    expect(select).toHaveBeenCalledWith("floor");
    expect(screen.queryByRole("button", { name: "Lab: Pending" })).not.toBeInTheDocument();
  });
});
