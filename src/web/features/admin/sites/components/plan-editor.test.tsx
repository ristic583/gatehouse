import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { FloorPlanDto, SecurityLevelAdminDto } from "@/lib/api/types";
import { PlanEditor } from "./plan-editor";

const levels: SecurityLevelAdminDto[] = [{ id: "lvl-1", name: "Standard", rank: 1, approverChain: "Manager", maxDurationDays: 365 }];

function plan(withArea: boolean): FloorPlanDto {
  return {
    floorId: "f1",
    siteId: "s1",
    name: "Ground",
    level: 0,
    planWidth: 1000,
    planHeight: 600,
    hasBackground: false,
    backgroundUpdatedAt: null,
    areas: withArea
      ? [
          {
            id: "a1",
            name: "Lab",
            code: "LAB",
            polygon: [
              { x: 100, y: 100 },
              { x: 300, y: 100 },
              { x: 300, y: 300 },
              { x: 100, y: 300 },
            ],
            securityLevelId: "lvl-1",
            owners: [{ personId: "p1", displayName: "Ada" }],
            doors: [],
          },
        ]
      : [],
  };
}

function renderEditor(withArea: boolean) {
  const onSave = vi.fn();
  render(<PlanEditor plan={plan(withArea)} securityLevels={levels} onSave={onSave} searchEmployees={() => Promise.resolve([])} />);
  return { canvas: screen.getByTestId("plan-editor-canvas"), onSave };
}

const click = (el: Element, x: number, y: number) => fireEvent.click(el, { clientX: x, clientY: y });

describe("PlanEditor", () => {
  it("draws a polygon by clicking points", () => {
    const { canvas } = renderEditor(false);
    fireEvent.click(screen.getByRole("button", { name: "Draw area" }));
    click(canvas, 102, 98);
    click(canvas, 304, 101);
    click(canvas, 299, 296);
    click(canvas, 101, 102); // closes on the first point

    expect(screen.getByTestId("editor-area-AREA-1")).toBeInTheDocument();
    const inspector = screen.getByTestId("area-inspector");
    expect(within(inspector).getByLabelText("Area 1 point 2 x")).toHaveValue(300);
    expect(within(inspector).getByLabelText("Area 1 point 3 y")).toHaveValue(300);
    expect(within(inspector).queryByLabelText("Area 1 point 4 x")).not.toBeInTheDocument();
  });

  it("snaps door to nearest edge", () => {
    const { canvas } = renderEditor(true);
    fireEvent.click(screen.getByRole("button", { name: "Place door" }));
    click(canvas, 190, 112);

    const door = screen.getByTestId("editor-door-Door 1");
    expect(door).toHaveAttribute("data-x", "190");
    expect(door).toHaveAttribute("data-y", "100");
    expect(screen.getByTestId("door-inspector")).toBeInTheDocument();
  });

  it("refuses self-intersecting polygon", () => {
    const { canvas } = renderEditor(false);
    fireEvent.click(screen.getByRole("button", { name: "Draw area" }));
    click(canvas, 100, 100);
    click(canvas, 300, 300);
    click(canvas, 300, 100);
    click(canvas, 100, 300);
    fireEvent.click(screen.getByRole("button", { name: "Finish area" }));

    expect(screen.getByTestId("plan-editor-message")).toHaveTextContent("must not cross itself");
    expect(screen.queryByTestId("editor-area-AREA-1")).not.toBeInTheDocument();
  });

  it("undo restores previous geometry", () => {
    renderEditor(true);
    fireEvent.click(screen.getByRole("button", { name: /^Lab/ }));
    fireEvent.click(screen.getByTestId("vertex-0"));
    fireEvent.keyDown(screen.getByTestId("plan-editor-canvas"), { key: "ArrowRight", shiftKey: true });
    expect(screen.getByLabelText("Lab point 1 x")).toHaveValue(110);

    fireEvent.click(screen.getByRole("button", { name: "Undo" }));
    expect(screen.getByLabelText("Lab point 1 x")).toHaveValue(100);
    fireEvent.click(screen.getByRole("button", { name: "Redo" }));
    expect(screen.getByLabelText("Lab point 1 x")).toHaveValue(110);
  });
});
