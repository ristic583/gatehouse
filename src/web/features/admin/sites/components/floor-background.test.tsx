import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { FloorMap } from "@/features/map/components/floor-map";
import type { FloorAccessMapDto } from "@/lib/api/types";
import { FloorBackground } from "./floor-background";

describe("FloorBackground", () => {
  it("rejects files over 10 MiB", () => {
    const upload = vi.fn(() => Promise.resolve());
    render(
      <FloorBackground
        floorId="f1"
        hasBackground={false}
        opacity={0.5}
        onOpacityChange={vi.fn()}
        onChanged={vi.fn()}
        upload={upload}
        remove={vi.fn()}
      />,
    );
    const big = new File(["x"], "plan.png", { type: "image/png" });
    Object.defineProperty(big, "size", { value: 10 * 1024 * 1024 + 1 });

    fireEvent.change(screen.getByTestId("floor-background"), { target: { files: [big] } });

    expect(screen.getByRole("alert")).toHaveTextContent("larger than 10 MiB");
    expect(upload).not.toHaveBeenCalled();
  });

  it("underlay toggles on the map", () => {
    const map: FloorAccessMapDto = {
      floorId: "f1",
      siteId: "s1",
      floorName: "Ground",
      planWidth: 1000,
      planHeight: 600,
      areas: [],
      hasBackground: true,
      backgroundUpdatedAt: "2026-11-14T10:00:00Z",
    };
    render(<FloorMap map={map} onAreaSelect={vi.fn()} />);

    const underlay = screen.getByTestId("plan-underlay");
    expect(underlay.getAttribute("href")).toBe("/api/proxy/v1/floors/f1/background?v=2026-11-14T10%3A00%3A00Z");
    fireEvent.click(screen.getByLabelText("Show drawing"));
    expect(screen.queryByTestId("plan-underlay")).not.toBeInTheDocument();
    fireEvent.click(screen.getByLabelText("Show drawing"));
    expect(screen.getByTestId("plan-underlay")).toBeInTheDocument();
  });
});
