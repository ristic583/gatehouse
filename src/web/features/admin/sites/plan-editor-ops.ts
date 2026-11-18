import type { PointDto } from "@/lib/api/types";
import { type Draft, type DraftArea, findDoor, mapArea, mapDoor } from "./draft";
import { nearestEdgePoint, pointInPolygon } from "./geometry";

export type EditorTool = "select" | "area" | "door" | "reader";

export type Selection =
  | { type: "area"; key: string }
  | { type: "vertex"; areaKey: string; index: number }
  | { type: "door"; key: string }
  | { type: "reader"; doorKey: string; key: string }
  | null;

export type DragTarget = { kind: "vertex"; areaKey: string; index: number } | { kind: "door"; key: string };

/** Distance (plan units) from the first point within which a click closes the polygon being drawn. */
export const CLOSE_DISTANCE = 8;

/** Distance (plan units) within which a door counts as lying on another area's wall. */
const SHARED_WALL_TOLERANCE = 1.5;

export const round2 = (p: PointDto) => ({ x: Math.round(p.x * 100) / 100, y: Math.round(p.y * 100) / 100 });

/** Names of the other areas whose boundary passes through the door position (a shared wall). */
export function sharedWallAreas(areas: readonly DraftArea[], ownKey: string, position: PointDto): string[] {
  return areas
    .filter((a) => a.key !== ownKey && a.polygon.length >= 2 && nearestEdgePoint(position, a.polygon).distance <= SHARED_WALL_TOLERANCE)
    .map((a) => a.name);
}

/** Moves a door (with its readers) to another area and snaps it onto that area's boundary. */
export function moveDoor(draft: Draft, doorKey: string, targetKey: string): Draft {
  const found = findDoor(draft, doorKey);
  const target = draft.areas.find((a) => a.key === targetKey);
  if (found === null || target === undefined || found.area.key === targetKey) return draft;
  const moved = { ...found.door, position: round2(nearestEdgePoint(found.door.position, target.polygon).point) };
  return {
    areas: draft.areas.map((a) =>
      a.key === found.area.key
        ? { ...a, doors: a.doors.filter((d) => d.key !== doorKey) }
        : a.key === targetKey
          ? { ...a, doors: [...a.doors, moved] }
          : a,
    ),
  };
}

/** Area edge where a door clicked at `click` lands, or null when there is no area yet. */
export function doorPlacement(areas: readonly DraftArea[], click: PointDto): { area: DraftArea; point: PointDto } | null {
  let best: { area: DraftArea; point: PointDto; distance: number } | null = null;
  for (const area of areas) {
    const edge = nearestEdgePoint(click, area.polygon);
    if (best === null || edge.distance < best.distance) best = { area, point: edge.point, distance: edge.distance };
  }
  if (best === null) return null;
  // On a shared wall the side that was clicked decides: the area containing the click wins (lab finding #7).
  const nearest = best;
  const clickedSide = areas.find(
    (a) => pointInPolygon(click, a.polygon) && nearestEdgePoint(click, a.polygon).distance <= nearest.distance + SHARED_WALL_TOLERANCE,
  );
  if (clickedSide !== undefined && clickedSide.key !== best.area.key) {
    return { area: clickedSide, point: nearestEdgePoint(click, clickedSide.polygon).point };
  }
  return { area: best.area, point: best.point };
}

/** Moves the selected item by `move` (vertex, whole area with doors and readers, door along its wall, or reader). */
export function nudgeSelection(d: Draft, selection: NonNullable<Selection>, move: (p: PointDto) => PointDto): Draft {
  if (selection.type === "vertex") {
    return mapArea(d, selection.areaKey, (a) => ({ ...a, polygon: a.polygon.map((p, i) => (i === selection.index ? move(p) : p)) }));
  }
  if (selection.type === "area") {
    return mapArea(d, selection.key, (a) => ({
      ...a,
      polygon: a.polygon.map(move),
      doors: a.doors.map((x) => ({
        ...x,
        position: move(x.position),
        readers: x.readers.map((r) => ({ ...r, position: move(r.position) })),
      })),
    }));
  }
  if (selection.type === "door") {
    const found = findDoor(d, selection.key);
    if (found === null) return d;
    return mapDoor(d, selection.key, (x) => ({ ...x, position: round2(nearestEdgePoint(move(x.position), found.area.polygon).point) }));
  }
  return mapDoor(d, selection.doorKey, (x) => ({
    ...x,
    readers: x.readers.map((r) => (r.key === selection.key ? { ...r, position: move(r.position) } : r)),
  }));
}

/** Draft with the dragged vertex or door moved to `point` (doors stay on their area's wall). */
export function dragTo(d: Draft, drag: DragTarget, point: PointDto): Draft {
  if (drag.kind === "vertex") {
    return mapArea(d, drag.areaKey, (a) => ({ ...a, polygon: a.polygon.map((p, i) => (i === drag.index ? point : p)) }));
  }
  const found = findDoor(d, drag.key);
  if (found === null) return d;
  return mapDoor(d, drag.key, (x) => ({ ...x, position: round2(nearestEdgePoint(point, found.area.polygon).point) }));
}
