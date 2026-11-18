import { fireEvent, render, screen } from "@testing-library/react";
import { useState } from "react";
import { describe, expect, it } from "vitest";
import type { PointDto } from "@/lib/api/types";
import { PointTable } from "./point-table";

function Harness() {
  const [points, setPoints] = useState<PointDto[]>([
    { x: 10, y: 10 },
    { x: 100, y: 10 },
    { x: 100, y: 100 },
  ]);
  return <PointTable label="Lab" points={points} onChange={(i, p) => setPoints((all) => all.map((q, j) => (j === i ? p : q)))} />;
}

describe("PointTable", () => {
  it("edits a vertex by keyboard", () => {
    render(<Harness />);
    const x = screen.getByLabelText("Lab point 2 x");
    x.focus();
    fireEvent.keyDown(x, { key: "ArrowUp" });
    expect(x).toHaveValue(101);
    fireEvent.keyDown(x, { key: "ArrowDown", shiftKey: true });
    expect(x).toHaveValue(91);
    const y = screen.getByLabelText("Lab point 3 y");
    fireEvent.change(y, { target: { value: "120" } });
    expect(y).toHaveValue(120);
  });
});
