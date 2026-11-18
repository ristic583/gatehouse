import { describe, expect, it } from "vitest";
import type { AreaAccessDto, FloorAccessMapDto } from "@/lib/api/types";
import { distanceFromWalls, floorElevation, pointInside, roomGeometry, siteScale, toPlanPoint } from "./geometry";

const area: AreaAccessDto = {
  areaId: "lab",
  code: "LAB",
  name: "Lab",
  state: "NoAccess",
  reason: "NoEntitlement",
  securityLevelName: "High",
  cardInactive: false,
  since: null,
  polygon: [
    { x: 0, y: 0 },
    { x: 100, y: 0 },
    { x: 100, y: 100 },
    { x: 0, y: 100 },
  ],
  doors: [],
};
const map: FloorAccessMapDto = { floorId: "f1", siteId: "s1", floorName: "Ground", planWidth: 100, planHeight: 100, areas: [area] };

describe("building geometry from recorded plans", () => {
  it("uses one site scale and preserves floor aspect ratios", () => {
    const wide = { ...map, planWidth: 1000, planHeight: 600 };
    const scale = siteScale([wide, { ...map, planWidth: 500, planHeight: 300 }]);
    const p = toPlanPoint({ x: 1000, y: 600 }, wide, scale);
    expect(p.x).toBeCloseTo(7);
    expect(p.z).toBeCloseTo(4.2);
    expect(500 * scale).toBeCloseTo(7);
    expect(siteScale([])).toBeGreaterThan(0);
  });

  it("cuts a real door opening without losing the rest of the wall", () => {
    const room = { ...area, doors: [{ doorId: "door", name: "Lab door", x: 50, y: 0 }] };
    const geo = roomGeometry(room, map, 0.1);
    expect(geo.doors).toHaveLength(1);
    expect(geo.doors[0]).toMatchObject({ x: 0, z: -5, width: 0.72 });
    expect(geo.walls.reduce((sum, w) => sum + w.length, 0)).toBeCloseTo(40 - 0.72);
    const bottom = geo.walls.filter((w) => w.z === -5);
    expect(bottom).toHaveLength(2);
    expect(bottom.every((w) => Math.abs(w.x) - w.length / 2 >= 0.36 - 1e-9)).toBe(true);
  });

  it("keeps off-boundary door markers at recorded coordinates without inventing an opening", () => {
    const geo = roomGeometry({ ...area, doors: [{ doorId: "d", name: "Unaligned", x: 40, y: 40 }] }, map, 0.1);
    expect(geo.doors[0]).toMatchObject({ x: -1, z: -1 });
    expect(geo.walls.reduce((sum, w) => sum + w.length, 0)).toBeCloseTo(40);
  });

  it("merges overlapping openings and handles duplicate vertices", () => {
    const geo = roomGeometry(
      {
        ...area,
        polygon: [...area.polygon, area.polygon[0]!],
        doors: [
          { doorId: "d1", name: "A", x: 50, y: 0 },
          { doorId: "d2", name: "B", x: 52, y: 0 },
        ],
      },
      map,
      0.1,
    );
    expect(geo.walls.every((w) => Number.isFinite(w.length) && w.length > 0)).toBe(true);
    expect(geo.walls.reduce((sum, w) => sum + w.length, 0)).toBeCloseTo(40 - 0.92);
  });

  it("keeps decorative interiors and labels inside a concave room, clear of doors and walls", () => {
    const room = {
      ...area,
      polygon: [
        { x: 0, y: 0 },
        { x: 100, y: 0 },
        { x: 100, y: 35 },
        { x: 35, y: 35 },
        { x: 35, y: 100 },
        { x: 0, y: 100 },
      ],
      doors: [{ doorId: "d", name: "Entry", x: 10, y: 0 }],
    };
    const geo = roomGeometry(room, map, 0.1);
    expect(geo.furniture.length).toBeGreaterThan(0);
    expect(pointInside(geo.center, geo.points)).toBe(true);
    for (const p of geo.furniture) {
      expect(pointInside(p, geo.points)).toBe(true);
      expect(distanceFromWalls(p, geo.points)).toBeGreaterThan(0.7);
      expect(geo.doors.every((d) => Math.hypot(d.x - p.x, d.z - p.z) > 1)).toBe(true);
    }
    expect(pointInside({ x: 3, z: 3 }, geo.points)).toBe(false);
  });

  it("does not produce invalid walls or props for an absent polygon", () => {
    const geo = roomGeometry({ ...area, polygon: [] }, map, 0.1);
    expect(geo.walls).toEqual([]);
    expect(geo.furniture).toEqual([]);
    expect(geo.center).toEqual({ x: 0, z: 0 });
  });

  it("separates exploded floors and anchors the selected isolated floor", () => {
    expect(floorElevation(2, 2, "isolated")).toBe(0);
    expect(floorElevation(2, 0, "exploded")).toBeGreaterThan(floorElevation(2, 0, "assembled"));
  });
});
