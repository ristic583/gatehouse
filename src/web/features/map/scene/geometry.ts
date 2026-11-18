import type { AreaAccessDto, DoorMarkerDto, FloorAccessMapDto, PointDto } from "@/lib/api/types";

export interface PlanPoint {
  x: number;
  z: number;
}
export interface WallSegment {
  x: number;
  z: number;
  length: number;
  rotation: number;
}
export interface DoorPlacement extends WallSegment {
  door: DoorMarkerDto;
  width: number;
}
export interface RoomGeometry {
  points: PlanPoint[];
  center: PlanPoint;
  bounds: { minX: number; maxX: number; minZ: number; maxZ: number };
  walls: WallSegment[];
  doors: DoorPlacement[];
  furniture: PlanPoint[];
}
export type BuildingView = "assembled" | "exploded" | "isolated";

/** One scale for a site's floors preserves their relative size and the recorded aspect ratio. */
export function siteScale(maps: FloorAccessMapDto[]): number {
  return 14 / Math.max(100, ...maps.flatMap((m) => [m.planWidth, m.planHeight]));
}
export function toPlanPoint(p: PointDto, map: FloorAccessMapDto, scale: number): PlanPoint {
  return { x: (p.x - map.planWidth / 2) * scale, z: (p.y - map.planHeight / 2) * scale };
}
export function pointInside(p: PlanPoint, polygon: PlanPoint[]): boolean {
  let inside = false;
  for (let i = 0, j = polygon.length - 1; i < polygon.length; j = i++) {
    const a = polygon[i]!;
    const b = polygon[j]!;
    if (a.z > p.z !== b.z > p.z && p.x < ((b.x - a.x) * (p.z - a.z)) / (b.z - a.z) + a.x) inside = !inside;
  }
  return inside;
}
function projection(p: PlanPoint, a: PlanPoint, b: PlanPoint) {
  const dx = b.x - a.x;
  const dz = b.z - a.z;
  const length = Math.hypot(dx, dz);
  const along = length ? Math.max(0, Math.min(length, ((p.x - a.x) * dx + (p.z - a.z) * dz) / length)) : 0;
  const x = a.x + (dx * along) / (length || 1);
  const z = a.z + (dz * along) / (length || 1);
  return { along, x, z, length, distance: Math.hypot(p.x - x, p.z - z), rotation: -Math.atan2(dz, dx) };
}
export function distanceFromWalls(p: PlanPoint, points: PlanPoint[]): number {
  return Math.min(...points.map((a, i) => projection(p, a, points[(i + 1) % points.length]!).distance));
}
/** Cut openings only where an actual door marker lies close to a room boundary. */
export function roomGeometry(area: AreaAccessDto, map: FloorAccessMapDto, scale: number): RoomGeometry {
  const points = area.polygon.map((p) => toPlanPoint(p, map, scale));
  const bounds = {
    minX: Math.min(...points.map((p) => p.x)),
    maxX: Math.max(...points.map((p) => p.x)),
    minZ: Math.min(...points.map((p) => p.z)),
    maxZ: Math.max(...points.map((p) => p.z)),
  };
  const walls: WallSegment[] = [];
  const doors: DoorPlacement[] = [];
  const doorEdges = area.doors.map((door) => {
    const p = toPlanPoint(door, map, scale);
    const edges = points.map((a, i) => ({ ...projection(p, a, points[(i + 1) % points.length]!), index: i }));
    const nearest = edges.sort((a, b) => a.distance - b.distance)[0];
    return { door, p, nearest };
  });
  points.forEach((a, i) => {
    const b = points[(i + 1) % points.length]!;
    const edge = projection(a, a, b);
    if (edge.length < 0.01) return;
    const gaps = doorEdges
      .filter((d) => d.nearest?.index === i && d.nearest.distance <= 0.3)
      .map((d) => {
        const width = Math.min(0.72, edge.length * 0.55);
        const at = Math.max(width / 2, Math.min(edge.length - width / 2, d.nearest!.along));
        const start = at - width / 2;
        const end = at + width / 2;
        doors.push({
          door: d.door,
          width,
          length: width,
          rotation: edge.rotation,
          x: a.x + ((b.x - a.x) * at) / edge.length,
          z: a.z + ((b.z - a.z) * at) / edge.length,
        });
        return { start, end };
      })
      .sort((a, b) => a.start - b.start);
    let from = 0;
    function segment(to: number) {
      if (to - from > 0.015)
        walls.push({
          x: a.x + ((b.x - a.x) * (from + to)) / (2 * edge.length),
          z: a.z + ((b.z - a.z) * (from + to)) / (2 * edge.length),
          length: to - from,
          rotation: edge.rotation,
        });
    }
    for (const gap of gaps) {
      segment(gap.start);
      from = Math.max(from, gap.end);
    }
    segment(edge.length);
  });
  // Markers not on a boundary remain at their recorded location; never invent a connection.
  doorEdges
    .filter((d) => !d.nearest || d.nearest.distance > 0.3)
    .forEach((d) => doors.push({ door: d.door, x: d.p.x, z: d.p.z, length: 0.6, width: 0.6, rotation: 0 }));
  const average = {
    x: points.reduce((s, p) => s + p.x, 0) / (points.length || 1),
    z: points.reduce((s, p) => s + p.z, 0) / (points.length || 1),
  };
  const candidates: PlanPoint[] = [];
  for (let x = bounds.minX + 0.7; x < bounds.maxX - 0.5; x += 1.35) {
    for (let z = bounds.minZ + 0.7; z < bounds.maxZ - 0.5; z += 1.3) {
      const p = { x, z };
      if (pointInside(p, points) && distanceFromWalls(p, points) > 0.7 && doors.every((d) => Math.hypot(d.x - x, d.z - z) > 1))
        candidates.push(p);
    }
  }
  const center = pointInside(average, points)
    ? average
    : ([...candidates].sort((a, b) => distanceFromWalls(b, points) - distanceFromWalls(a, points))[0] ?? points[0] ?? { x: 0, z: 0 });
  return { points, center, bounds, walls, doors, furniture: candidates.slice(0, 9) };
}
export function floorElevation(index: number, selectedIndex: number, view: BuildingView): number {
  return view === "isolated" ? (index - selectedIndex) * 4.1 : index * (view === "exploded" ? 3.25 : 1.7);
}
export function interiorKind(name: string): "workstations" | "meeting" | "racks" | "lounge" | "storage" {
  if (/server|data|comms|plant/i.test(name)) return "racks";
  if (/meeting|board|conference/i.test(name)) return "meeting";
  if (/lounge|café|cafe|atrium|welcome|reception|terrace/i.test(name)) return "lounge";
  if (/archive|storage|delivery|vault/i.test(name)) return "storage";
  return "workstations";
}
