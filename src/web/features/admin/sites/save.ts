import { ApiError } from "@/lib/api/client";
import { sitesAdminApi, sitesAdminError } from "./api";
import { type Draft, type DraftArea, type DraftDoor, type DraftReader, diffDraft, type SaveOp } from "./draft";

export type SitesApi = Pick<
  typeof sitesAdminApi,
  | "createArea"
  | "updateArea"
  | "deleteArea"
  | "setOwners"
  | "createDoor"
  | "updateDoor"
  | "deleteDoor"
  | "createReader"
  | "updateReader"
  | "deleteReader"
>;

export interface SaveOutcome {
  /** Error text per draft key (area, door or reader). */
  errors: Record<string, string>;
  /** The new server state as a draft: the current draft with new ids, failed changes rolled back. */
  original: Draft;
  /** The current draft with ids of created entities filled in. */
  current: Draft;
}

const ref = (value: string) => (value.trim() === "" ? null : value.trim());
const areaBody = (a: DraftArea, withOwners: boolean) => ({
  name: a.name.trim(),
  code: a.code.trim(),
  polygon: a.polygon,
  securityLevelId: a.securityLevelId,
  ownerIds: withOwners ? a.owners.map((o) => o.personId) : null,
  accessControlled: a.accessControlled ?? true,
});
const doorBody = (d: DraftDoor) => ({ name: d.name.trim(), position: d.position, pacsDoorRef: ref(d.pacsDoorRef) });
const readerBody = (r: DraftReader) => ({
  name: r.name.trim(),
  direction: r.direction,
  position: r.position,
  pacsReaderRef: ref(r.pacsReaderRef),
});

/** Sends one request per changed entity and collects server errors per entity (never stops at the first failure). */
export async function runSave(floorId: string, original: Draft, current: Draft, api: SitesApi = sitesAdminApi): Promise<SaveOutcome> {
  const ops = diffDraft(original, current);
  const errors: Record<string, string> = {};
  const ids = new Map<string, string>();
  const failed = new Map<string, SaveOp["kind"]>();
  const idOf = (key: string, id: string | null) => id ?? ids.get(key) ?? null;
  const areaIdOfKey = (key: string) => {
    const area = current.areas.find((a) => a.key === key);
    return area === undefined ? null : idOf(area.key, area.id);
  };
  const doorIdOfKey = (key: string) => {
    const door = current.areas.flatMap((a) => a.doors).find((d) => d.key === key);
    return door === undefined ? null : idOf(door.key, door.id);
  };

  for (const op of ops) {
    try {
      switch (op.kind) {
        case "deleteReader":
          await api.deleteReader(op.id);
          break;
        case "deleteDoor":
          await api.deleteDoor(op.id);
          break;
        case "deleteArea":
          await api.deleteArea(op.id);
          break;
        case "createArea":
          ids.set(op.key, (await api.createArea(floorId, areaBody(op.area, true))).id);
          break;
        case "updateArea":
          await api.updateArea(op.area.id!, areaBody(op.area, false));
          break;
        case "setOwners":
          await api.setOwners(
            op.area.id!,
            op.area.owners.map((o) => o.personId),
          );
          break;
        case "createDoor": {
          const areaId = areaIdOfKey(op.areaKey);
          if (areaId === null) throw new Error("Save the area first.");
          ids.set(op.key, (await api.createDoor(areaId, doorBody(op.door))).id);
          break;
        }
        case "updateDoor": {
          const areaId = op.moved ? areaIdOfKey(op.areaKey) : null;
          if (op.moved && areaId === null) throw new Error("Save the area first.");
          await api.updateDoor(op.door.id!, { ...doorBody(op.door), areaId });
          break;
        }
        case "createReader": {
          const doorId = doorIdOfKey(op.doorKey);
          if (doorId === null) throw new Error("Save the door first.");
          ids.set(op.key, (await api.createReader(doorId, readerBody(op.reader))).id);
          break;
        }
        case "updateReader":
          await api.updateReader(op.reader.id!, readerBody(op.reader));
          break;
      }
    } catch (error) {
      failed.set(op.key, op.kind);
      errors[op.key] =
        error instanceof ApiError ? sitesAdminError(error.code, error.detail) : error instanceof Error ? error.message : "Save failed.";
    }
  }

  const withIds: Draft = {
    areas: current.areas.map((a) => ({
      ...a,
      id: idOf(a.key, a.id),
      doors: a.doors.map((d) => ({ ...d, id: idOf(d.key, d.id), readers: d.readers.map((r) => ({ ...r, id: idOf(r.key, r.id) })) })),
    })),
  };
  return { errors, current: withIds, original: rebase(original, withIds, failed) };
}

/** Server state after a partial save: successful changes applied, failed ones as they were before. */
function rebase(before: Draft, after: Draft, failed: Map<string, SaveOp["kind"]>): Draft {
  const oldArea = new Map(before.areas.map((a) => [a.key, a]));
  const oldDoor = new Map(before.areas.flatMap((a) => a.doors.map((d) => [d.key, d] as const)));
  const oldReader = new Map(before.areas.flatMap((a) => a.doors.flatMap((d) => d.readers.map((r) => [r.key, r] as const))));
  const kind = (key: string) => failed.get(key);

  const areas: DraftArea[] = [];
  for (const a of after.areas) {
    if (kind(a.key) === "createArea") continue;
    const prev = oldArea.get(a.key);
    const base =
      kind(a.key) === "updateArea" && prev !== undefined
        ? { ...a, name: prev.name, code: prev.code, polygon: prev.polygon, securityLevelId: prev.securityLevelId }
        : a;
    const owners = kind(a.key) === "setOwners" && prev !== undefined ? prev.owners : base.owners;
    const doors: DraftDoor[] = [];
    for (const d of a.doors) {
      if (kind(d.key) === "createDoor") continue;
      const prevDoor = oldDoor.get(d.key);
      const door =
        kind(d.key) === "updateDoor" && prevDoor !== undefined
          ? { ...d, name: prevDoor.name, position: prevDoor.position, pacsDoorRef: prevDoor.pacsDoorRef }
          : d;
      const readers = d.readers
        .filter((r) => kind(r.key) !== "createReader")
        .map((r) => {
          const prevReader = oldReader.get(r.key);
          return kind(r.key) === "updateReader" && prevReader !== undefined ? prevReader : r;
        });
      prevDoor?.readers.forEach((r) => {
        if (kind(r.key) === "deleteReader") readers.push(r);
      });
      doors.push({ ...door, readers });
    }
    prev?.doors.forEach((d) => {
      if (kind(d.key) === "deleteDoor") doors.push(d);
    });
    areas.push({ ...base, owners, doors });
  }
  before.areas.forEach((a) => {
    if (kind(a.key) === "deleteArea") areas.push(a);
  });
  return { areas };
}
