import { describe, expect, it } from "vitest";
import { type Draft, type DraftArea, diffDraft } from "../draft";
import { moveDoor, sharedWallAreas } from "./plan-editor";

function area(key: string, name: string, x0: number, x1: number, doors: DraftArea["doors"] = []): DraftArea {
  return {
    key,
    id: `${key}-id`,
    name,
    code: key.toUpperCase(),
    polygon: [
      { x: x0, y: 0 },
      { x: x1, y: 0 },
      { x: x1, y: 10 },
      { x: x0, y: 10 },
    ],
    securityLevelId: "l",
    owners: [],
    doors,
  };
}

const door = { key: "d1", id: "d1-id", name: "Room door", position: { x: 10, y: 5 }, pacsDoorRef: "D-1", readers: [] };
const plan: Draft = { areas: [area("corridor", "Corridor", 0, 10, [door]), area("room", "Room", 10, 20)] };

describe("door area (lab finding #7)", () => {
  it("names the other area of a shared wall", () => {
    expect(sharedWallAreas(plan.areas, "corridor", door.position)).toEqual(["Room"]);
    expect(sharedWallAreas(plan.areas, "corridor", { x: 0, y: 5 })).toEqual([]);
  });

  it("moves the door with its readers to the chosen area and saves it as a move", () => {
    const moved = moveDoor(plan, "d1", "room");

    expect(moved.areas[0]?.doors).toHaveLength(0);
    expect(moved.areas[1]?.doors[0]).toMatchObject({ key: "d1", id: "d1-id", position: { x: 10, y: 5 } });
    expect(diffDraft(plan, moved)).toEqual([expect.objectContaining({ kind: "updateDoor", key: "d1", areaKey: "room", moved: true })]);
  });
});
