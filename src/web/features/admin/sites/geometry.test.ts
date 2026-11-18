import { describe, expect, it } from "vitest";
import { distanceToEdge, isSelfIntersecting, nearestEdgePoint, overlapRatio, polygonProblem, snapPoint } from "./geometry";

const square = [
  { x: 100, y: 100 },
  { x: 300, y: 100 },
  { x: 300, y: 300 },
  { x: 100, y: 300 },
];

describe("editor geometry", () => {
  it("door snap stays on edge", () => {
    for (const click of [
      { x: 190, y: 112 },
      { x: 320, y: 250 },
      { x: 50, y: 50 },
      { x: 200, y: 260 },
    ]) {
      const snap = nearestEdgePoint(click, square);
      const a = square[snap.edgeIndex]!;
      const b = square[(snap.edgeIndex + 1) % square.length]!;
      expect(distanceToEdge(snap.point, a, b)).toBeLessThan(1e-9);
    }
    expect(nearestEdgePoint({ x: 190, y: 112 }, square).point).toEqual({ x: 190, y: 100 });
  });

  it("detects self-intersection and overlap", () => {
    expect(isSelfIntersecting(square)).toBe(false);
    expect(
      isSelfIntersecting([
        { x: 0, y: 0 },
        { x: 100, y: 100 },
        { x: 100, y: 0 },
        { x: 0, y: 100 },
      ]),
    ).toBe(true);
    expect(
      polygonProblem(
        [
          { x: 0, y: 0 },
          { x: 10, y: 0 },
        ],
        500,
        500,
      ),
    ).toBe("area.invalid_polygon");
    expect(polygonProblem(square, 200, 200)).toBe("area.invalid_polygon");
    const shifted = square.map((p) => ({ x: p.x + 100, y: p.y }));
    expect(overlapRatio(square, shifted)).toBeCloseTo(0.5, 1);
    expect(polygonProblem(shifted, 500, 500, [square])).toBe("area.overlaps");
    expect(
      polygonProblem(
        square.map((p) => ({ x: p.x + 200, y: p.y })),
        600,
        500,
        [square],
      ),
    ).toBeNull();
  });

  it("snaps to the 10-unit grid", () => {
    expect(snapPoint({ x: 104, y: 96 }, true)).toEqual({ x: 100, y: 100 });
    expect(snapPoint({ x: 104.4, y: 96.6 }, false)).toEqual({ x: 104, y: 97 });
  });
});
