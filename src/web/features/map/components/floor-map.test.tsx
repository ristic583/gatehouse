import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { AreaAccessDto, FloorAccessMapDto } from "@/lib/api/types";
import { FloorMap } from "./floor-map";

function area(code: string, name: string, state: AreaAccessDto["state"], cardInactive = false): AreaAccessDto {
  return {
    areaId: code,
    name,
    code,
    polygon: [
      { x: 0, y: 0 },
      { x: 100, y: 0 },
      { x: 100, y: 100 },
      { x: 0, y: 100 },
    ],
    securityLevelName: "Low",
    state,
    reason: "NoEntitlement",
    cardInactive,
    since: null,
    doors: [],
  };
}

const map: FloorAccessMapDto = {
  floorId: "f",
  siteId: "s",
  floorName: "Second floor",
  planWidth: 1000,
  planHeight: 600,
  areas: [
    area("A-OK", "Office", "HasAccess", true),
    area("A-NO", "Server", "NoAccess"),
    area("A-PEND", "Vault", "Pending"),
    area("A-UNK", "Plant", "Unknown"),
  ],
};

describe("FloorMap", () => {
  it("renders one area per dto with data-state", () => {
    render(<FloorMap map={map} onAreaSelect={() => {}} />);
    expect(screen.getByTestId("area-A-OK")).toHaveAttribute("data-state", "HasAccess");
    expect(screen.getByTestId("area-A-NO")).toHaveAttribute("data-state", "NoAccess");
    expect(screen.getByTestId("area-A-PEND")).toHaveAttribute("data-state", "Pending");
    expect(screen.getByTestId("area-A-UNK")).toHaveAttribute("data-state", "Unknown");
  });

  it("area has accessible name with state label", () => {
    render(<FloorMap map={map} onAreaSelect={() => {}} />);
    expect(screen.getByRole("button", { name: "Vault: Pending" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Server: No access" })).toBeInTheDocument();
  });

  it("pending area uses hatch pattern", () => {
    render(<FloorMap map={map} onAreaSelect={() => {}} />);
    expect(screen.getByTestId("hatch-A-PEND")).toHaveAttribute("fill", "url(#hatch-pending)");
    expect(screen.queryByTestId("hatch-A-NO")).not.toBeInTheDocument();
  });

  it("click and Enter call onAreaSelect", () => {
    const onSelect = vi.fn();
    render(<FloorMap map={map} onAreaSelect={onSelect} />);
    fireEvent.click(screen.getByTestId("area-A-NO"));
    fireEvent.keyDown(screen.getByTestId("area-A-UNK"), { key: "Enter" });
    expect(onSelect).toHaveBeenCalledTimes(2);
    expect(onSelect.mock.calls[0]?.[0]).toMatchObject({ code: "A-NO" });
    expect(onSelect.mock.calls[1]?.[0]).toMatchObject({ code: "A-UNK" });
  });

  it("card inactive note is shown", () => {
    render(<FloorMap map={map} onAreaSelect={() => {}} />);
    expect(screen.getByText("assigned access; card inactive")).toBeInTheDocument();
  });
});
