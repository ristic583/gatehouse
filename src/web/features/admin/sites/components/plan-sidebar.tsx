"use client";

import type { PersonSummaryDto, SecurityLevelAdminDto } from "@/lib/api/types";
import { type DraftArea, type DraftDoor, mapArea, mapDoor } from "../draft";
import { nearestEdgePoint } from "../geometry";
import type { PlanEditorState } from "../hooks/use-plan-editor";
import { moveDoor, round2, sharedWallAreas } from "../plan-editor-ops";
import { AreaInspector, DoorInspector } from "./plan-inspector";

interface PlanSidebarProps {
  editor: PlanEditorState;
  selectedAreaKey: string | null;
  selectedArea: DraftArea | null;
  selectedDoor: { area: DraftArea; door: DraftDoor } | null;
  securityLevels: readonly SecurityLevelAdminDto[];
  searchEmployees: (query: string) => Promise<PersonSummaryDto[]>;
}

/** Area list plus the inspector of the selected area or door. */
export function PlanSidebar({ editor, selectedAreaKey, selectedArea, selectedDoor, securityLevels, searchEmployees }: PlanSidebarProps) {
  const { draft, entityErrors, apply } = editor;
  return (
    <aside className="flex w-full flex-col gap-3 lg:w-80" aria-label="Inspector" data-testid="plan-inspector">
      <ul className="flex flex-col gap-1 text-sm" aria-label="Areas">
        {draft.areas.map((a) => (
          <li key={a.key}>
            <button
              type="button"
              className={`w-full rounded px-2 py-1 text-left ${a.key === selectedAreaKey ? "bg-muted" : ""}`}
              onClick={() => editor.setSelection({ type: "area", key: a.key })}
            >
              {a.name} <span className="text-muted-foreground">{a.code}</span>
              {(entityErrors[a.key] !== undefined ||
                a.doors.some((d) => entityErrors[d.key] !== undefined || d.readers.some((r) => entityErrors[r.key] !== undefined))) && (
                <span className="text-destructive"> !</span>
              )}
            </button>
          </li>
        ))}
      </ul>
      {selectedArea !== null && (
        <AreaInspector
          area={selectedArea}
          error={entityErrors[selectedArea.key]}
          securityLevels={securityLevels}
          searchEmployees={searchEmployees}
          onChange={(next) => apply((d) => mapArea(d, selectedArea.key, () => next))}
          onDelete={() => {
            apply((d) => ({ areas: d.areas.filter((a) => a.key !== selectedArea.key) }));
            editor.setSelection(null);
          }}
        />
      )}
      {selectedDoor !== null && (
        <DoorInspector
          door={selectedDoor.door}
          errors={entityErrors}
          onChange={(next) =>
            apply((d) =>
              mapDoor(d, selectedDoor.door.key, () => ({
                ...next,
                position: round2(nearestEdgePoint(next.position, selectedDoor.area.polygon).point),
              })),
            )
          }
          onChangeReaders={(readers) => apply((d) => mapDoor(d, selectedDoor.door.key, (x) => ({ ...x, readers })))}
          onDelete={() => {
            apply((d) =>
              mapArea(d, selectedDoor.area.key, (a) => ({ ...a, doors: a.doors.filter((x) => x.key !== selectedDoor.door.key) })),
            );
            editor.setSelection(null);
          }}
          onAddReader={() => editor.placeReader({ x: selectedDoor.door.position.x + 10, y: selectedDoor.door.position.y + 10 })}
          areas={draft.areas.map((a) => ({ key: a.key, name: a.name }))}
          areaKey={selectedDoor.area.key}
          sharedWith={sharedWallAreas(draft.areas, selectedDoor.area.key, selectedDoor.door.position)}
          onMoveToArea={(target) => apply((d) => moveDoor(d, selectedDoor.door.key, target))}
        />
      )}
    </aside>
  );
}
