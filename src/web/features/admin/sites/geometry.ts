import type { PointDto } from "@/lib/api/types";

/** Client mirrors of the server floor-plan rules (`domain-model.md` §2.1 + M10 admin geometry); the server stays authoritative. */
export const GRID = 10;

export function snapValue(value: number, grid = GRID): number {
  return Math.round(value / grid) * grid;
}

export function snapPoint(p: PointDto, enabled: boolean, grid = GRID): PointDto {
  return enabled ? { x: snapValue(p.x, grid), y: snapValue(p.y, grid) } : { x: Math.round(p.x), y: Math.round(p.y) };
}

export function clampToPlan(p: PointDto, width: number, height: number): PointDto {
  return { x: Math.min(width, Math.max(0, p.x)), y: Math.min(height, Math.max(0, p.y)) };
}

export function insidePlan(p: PointDto, width: number, height: number): boolean {
  return p.x >= 0 && p.y >= 0 && p.x <= width && p.y <= height;
}

function cross(o: PointDto, a: PointDto, b: PointDto): number {
  return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
}

function onSegment(p: PointDto, a: PointDto, b: PointDto): boolean {
  return Math.min(a.x, b.x) <= p.x && p.x <= Math.max(a.x, b.x) && Math.min(a.y, b.y) <= p.y && p.y <= Math.max(a.y, b.y);
}

export function segmentsIntersect(a: PointDto, b: PointDto, c: PointDto, d: PointDto): boolean {
  const d1 = cross(c, d, a);
  const d2 = cross(c, d, b);
  const d3 = cross(a, b, c);
  const d4 = cross(a, b, d);
  if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
  return (
    (d1 === 0 && onSegment(a, c, d)) ||
    (d2 === 0 && onSegment(b, c, d)) ||
    (d3 === 0 && onSegment(c, a, b)) ||
    (d4 === 0 && onSegment(d, a, b))
  );
}

/** True when two non-adjacent edges touch or cross. */
export function isSelfIntersecting(points: readonly PointDto[]): boolean {
  const n = points.length;
  for (let i = 0; i < n; i++) {
    const a = points[i]!;
    const b = points[(i + 1) % n]!;
    for (let j = i + 1; j < n; j++) {
      if (j === i || (j + 1) % n === i || j === (i + 1) % n) continue;
      if (segmentsIntersect(a, b, points[j]!, points[(j + 1) % n]!)) return true;
    }
  }
  return false;
}

export function polygonArea(points: readonly PointDto[]): number {
  let sum = 0;
  for (let i = 0; i < points.length; i++) {
    const a = points[i]!;
    const b = points[(i + 1) % points.length]!;
    sum += a.x * b.y - b.x * a.y;
  }
  return Math.abs(sum) / 2;
}

export function pointInPolygon(p: PointDto, polygon: readonly PointDto[]): boolean {
  let inside = false;
  for (let i = 0, j = polygon.length - 1; i < polygon.length; j = i++) {
    const a = polygon[i]!;
    const b = polygon[j]!;
    if (a.y > p.y !== b.y > p.y && p.x < ((b.x - a.x) * (p.y - a.y)) / (b.y - a.y) + a.x) inside = !inside;
  }
  return inside;
}

/** Overlap as a share of the smaller polygon, by grid sampling (the server samples 200×200). */
export function overlapRatio(a: readonly PointDto[], b: readonly PointDto[], samples = 100): number {
  const minX = Math.max(Math.min(...a.map((p) => p.x)), Math.min(...b.map((p) => p.x)));
  const maxX = Math.min(Math.max(...a.map((p) => p.x)), Math.max(...b.map((p) => p.x)));
  const minY = Math.max(Math.min(...a.map((p) => p.y)), Math.min(...b.map((p) => p.y)));
  const maxY = Math.min(Math.max(...a.map((p) => p.y)), Math.max(...b.map((p) => p.y)));
  if (maxX <= minX || maxY <= minY) return 0;
  const dx = (maxX - minX) / samples;
  const dy = (maxY - minY) / samples;
  let hits = 0;
  for (let i = 0; i < samples; i++) {
    for (let j = 0; j < samples; j++) {
      const p = { x: minX + (i + 0.5) * dx, y: minY + (j + 0.5) * dy };
      if (pointInPolygon(p, a) && pointInPolygon(p, b)) hits++;
    }
  }
  const overlap = hits * dx * dy;
  const smaller = Math.min(polygonArea(a), polygonArea(b));
  return smaller === 0 ? 0 : overlap / smaller;
}

export type PolygonProblem = "area.invalid_polygon" | "area.overlaps";

/** Returns the server error code the polygon would get, or null. */
export function polygonProblem(
  points: readonly PointDto[],
  width: number,
  height: number,
  others: readonly (readonly PointDto[])[] = [],
): PolygonProblem | null {
  if (points.length < 3 || points.some((p) => !insidePlan(p, width, height)) || isSelfIntersecting(points) || polygonArea(points) === 0) {
    return "area.invalid_polygon";
  }
  if (others.some((other) => other.length >= 3 && overlapRatio(points, other) > 0.01)) return "area.overlaps";
  return null;
}

export const PROBLEM_TEXT: Record<PolygonProblem, string> = {
  "area.invalid_polygon": "The polygon needs at least 3 points inside the plan and must not cross itself.",
  "area.overlaps": "The area overlaps another area on this floor.",
};

export interface EdgeSnap {
  point: PointDto;
  edgeIndex: number;
  distance: number;
}

/** Nearest point on the polygon boundary; doors placed there line up with the 3D door gaps (`scene/geometry.ts`). */
export function nearestEdgePoint(p: PointDto, polygon: readonly PointDto[]): EdgeSnap {
  let best: EdgeSnap = { point: p, edgeIndex: -1, distance: Number.POSITIVE_INFINITY };
  for (let i = 0; i < polygon.length; i++) {
    const a = polygon[i]!;
    const b = polygon[(i + 1) % polygon.length]!;
    const dx = b.x - a.x;
    const dy = b.y - a.y;
    const lengthSq = dx * dx + dy * dy;
    const t = lengthSq === 0 ? 0 : Math.max(0, Math.min(1, ((p.x - a.x) * dx + (p.y - a.y) * dy) / lengthSq));
    const point = { x: a.x + t * dx, y: a.y + t * dy };
    const distance = Math.hypot(p.x - point.x, p.y - point.y);
    if (distance < best.distance) best = { point, edgeIndex: i, distance };
  }
  return best;
}

export function distanceToEdge(p: PointDto, a: PointDto, b: PointDto): number {
  return nearestEdgePoint(p, [a, b]).distance;
}

/** Index after which a new vertex is inserted for a click near an edge (midpoint handles). */
export function midpoint(a: PointDto, b: PointDto): PointDto {
  return { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 };
}
