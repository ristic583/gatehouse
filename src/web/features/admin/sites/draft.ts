import type { FloorPlanDto, PersonRefDto, PointDto, ReaderDirection } from "@/lib/api/types";

/** Editable copy of a `FloorPlanDto`; `id` null = not saved yet, `key` is stable across saves within the editor. */
export interface DraftReader {
  key: string;
  id: string | null;
  name: string;
  direction: ReaderDirection;
  position: PointDto;
  pacsReaderRef: string;
}

export interface DraftDoor {
  key: string;
  id: string | null;
  name: string;
  position: PointDto;
  pacsDoorRef: string;
  readers: DraftReader[];
}

export interface DraftArea {
  key: string;
  id: string | null;
  name: string;
  code: string;
  polygon: PointDto[];
  securityLevelId: string;
  owners: PersonRefDto[];
  doors: DraftDoor[];
  /** Missing = true (access-controlled). */
  accessControlled?: boolean;
}

export interface Draft {
  areas: DraftArea[];
}

export const HISTORY_LIMIT = 50;

export interface History {
  past: Draft[];
  present: Draft;
  future: Draft[];
}

export function draftFromPlan(plan: FloorPlanDto): Draft {
  return {
    areas: plan.areas.map((a) => ({
      key: a.id,
      id: a.id,
      name: a.name,
      code: a.code,
      polygon: a.polygon.map((p) => ({ ...p })),
      securityLevelId: a.securityLevelId,
      owners: a.owners,
      accessControlled: a.accessControlled ?? true,
      doors: a.doors.map((d) => ({
        key: d.id,
        id: d.id,
        name: d.name,
        position: { ...d.position },
        pacsDoorRef: d.pacsDoorRef ?? "",
        readers: d.readers.map((r) => ({
          key: r.id,
          id: r.id,
          name: r.name,
          direction: r.direction,
          position: { ...r.position },
          pacsReaderRef: r.pacsReaderRef ?? "",
        })),
      })),
    })),
  };
}

export function commit(history: History, next: Draft): History {
  if (next === history.present) return history;
  return { past: [...history.past, history.present].slice(-HISTORY_LIMIT), present: next, future: [] };
}

export function undo(history: History): History {
  const previous = history.past.at(-1);
  if (previous === undefined) return history;
  return { past: history.past.slice(0, -1), present: previous, future: [history.present, ...history.future] };
}

export function redo(history: History): History {
  const [next, ...rest] = history.future;
  if (next === undefined) return history;
  return { past: [...history.past, history.present].slice(-HISTORY_LIMIT), present: next, future: rest };
}

let counter = 0;
export function newKey(prefix: string): string {
  counter += 1;
  return `new-${prefix}-${counter}`;
}

export function mapArea(draft: Draft, key: string, fn: (a: DraftArea) => DraftArea): Draft {
  return { areas: draft.areas.map((a) => (a.key === key ? fn(a) : a)) };
}

export function mapDoor(draft: Draft, doorKey: string, fn: (d: DraftDoor) => DraftDoor): Draft {
  return {
    areas: draft.areas.map((a) =>
      a.doors.some((d) => d.key === doorKey) ? { ...a, doors: a.doors.map((d) => (d.key === doorKey ? fn(d) : d)) } : a,
    ),
  };
}

export function findDoor(draft: Draft, doorKey: string): { area: DraftArea; door: DraftDoor } | null {
  for (const area of draft.areas) {
    const door = area.doors.find((d) => d.key === doorKey);
    if (door !== undefined) return { area, door };
  }
  return null;
}

/** One API call per changed entity; deletes first (readers → doors → areas), then areas, doors, readers. */
export type SaveOp =
  | { kind: "deleteReader"; key: string; id: string }
  | { kind: "deleteDoor"; key: string; id: string }
  | { kind: "deleteArea"; key: string; id: string }
  | { kind: "createArea"; key: string; area: DraftArea }
  | { kind: "updateArea"; key: string; area: DraftArea }
  | { kind: "setOwners"; key: string; area: DraftArea }
  | { kind: "createDoor"; key: string; areaKey: string; door: DraftDoor }
  | { kind: "updateDoor"; key: string; areaKey: string; door: DraftDoor; moved: boolean }
  | { kind: "createReader"; key: string; doorKey: string; reader: DraftReader }
  | { kind: "updateReader"; key: string; reader: DraftReader };

const same = (a: unknown, b: unknown) => JSON.stringify(a) === JSON.stringify(b);

export function diffDraft(original: Draft, current: Draft): SaveOp[] {
  const ops: SaveOp[] = [];
  const origAreas = new Map(original.areas.map((a) => [a.key, a]));
  const origDoors = new Map(original.areas.flatMap((a) => a.doors.map((d) => [d.key, d] as const)));
  const origDoorArea = new Map(original.areas.flatMap((a) => a.doors.map((d) => [d.key, a.key] as const)));
  const origReaders = new Map(original.areas.flatMap((a) => a.doors.flatMap((d) => d.readers.map((r) => [r.key, r] as const))));
  const curAreas = new Set(current.areas.map((a) => a.key));
  const curDoors = new Set(current.areas.flatMap((a) => a.doors.map((d) => d.key)));
  const curReaders = new Set(current.areas.flatMap((a) => a.doors.flatMap((d) => d.readers.map((r) => r.key))));

  for (const [key, r] of origReaders) if (!curReaders.has(key) && r.id !== null) ops.push({ kind: "deleteReader", key, id: r.id });
  for (const [key, d] of origDoors) if (!curDoors.has(key) && d.id !== null) ops.push({ kind: "deleteDoor", key, id: d.id });
  for (const [key, a] of origAreas) if (!curAreas.has(key) && a.id !== null) ops.push({ kind: "deleteArea", key, id: a.id });

  for (const area of current.areas) {
    const before = origAreas.get(area.key);
    if (area.id === null || before === undefined) {
      ops.push({ kind: "createArea", key: area.key, area });
    } else {
      if (
        !same(
          [area.name, area.code, area.polygon, area.securityLevelId, area.accessControlled ?? true],
          [before.name, before.code, before.polygon, before.securityLevelId, before.accessControlled ?? true],
        )
      )
        ops.push({ kind: "updateArea", key: area.key, area });
      if (!same(area.owners.map((o) => o.personId).sort(), before.owners.map((o) => o.personId).sort()))
        ops.push({ kind: "setOwners", key: area.key, area });
    }
    for (const door of area.doors) {
      const doorBefore = origDoors.get(door.key);
      if (door.id === null || doorBefore === undefined) ops.push({ kind: "createDoor", key: door.key, areaKey: area.key, door });
      else {
        const moved = origDoorArea.get(door.key) !== area.key;
        if (moved || !same([door.name, door.position, door.pacsDoorRef], [doorBefore.name, doorBefore.position, doorBefore.pacsDoorRef]))
          ops.push({ kind: "updateDoor", key: door.key, areaKey: area.key, door, moved });
      }
      for (const reader of door.readers) {
        const readerBefore = origReaders.get(reader.key);
        if (reader.id === null || readerBefore === undefined)
          ops.push({ kind: "createReader", key: reader.key, doorKey: door.key, reader });
        else if (
          !same(
            [reader.name, reader.direction, reader.position, reader.pacsReaderRef],
            [readerBefore.name, readerBefore.direction, readerBefore.position, readerBefore.pacsReaderRef],
          )
        )
          ops.push({ kind: "updateReader", key: reader.key, reader });
      }
    }
  }
  return ops;
}
