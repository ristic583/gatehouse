"use client";

import { useRef } from "react";
import type { FloorPlanDto, PersonSummaryDto, SecurityLevelAdminDto } from "@/lib/api/types";
import { type Draft, findDoor } from "../draft";
import { usePlanEditor } from "../hooks/use-plan-editor";
import type { SaveOutcome } from "../save";
import { PlanCanvas } from "./plan-canvas";
import { PlanEditorToolbar } from "./plan-editor-toolbar";
import { PlanSidebar } from "./plan-sidebar";

export { moveDoor, sharedWallAreas, type EditorTool } from "../plan-editor-ops";

export interface PlanEditorProps {
  plan: FloorPlanDto;
  securityLevels: readonly SecurityLevelAdminDto[];
  onSave: (original: Draft, current: Draft) => Promise<SaveOutcome>;
  searchEmployees: (query: string) => Promise<PersonSummaryDto[]>;
  backgroundUrl?: string | null;
  backgroundOpacity?: number;
}

/** SVG floor-plan editor (ADR-007): areas, doors on area edges, readers; local undo history of 50 steps. */
export function PlanEditor({ plan, securityLevels, onSave, searchEmployees, backgroundUrl, backgroundOpacity = 0.5 }: PlanEditorProps) {
  const svgRef = useRef<SVGSVGElement>(null);
  const editor = usePlanEditor(plan, securityLevels, onSave, svgRef);
  const { draft, selection, problems, message } = editor;

  const selectedAreaKey = selection?.type === "area" ? selection.key : selection?.type === "vertex" ? selection.areaKey : null;
  const selectedArea = draft.areas.find((a) => a.key === selectedAreaKey) ?? null;
  const selectedDoorKey = selection?.type === "door" ? selection.key : selection?.type === "reader" ? selection.doorKey : null;
  const selectedDoor = selectedDoorKey !== null ? findDoor(draft, selectedDoorKey) : null;
  const hasProblems = Object.keys(problems).length > 0;

  return (
    <div className="flex flex-col gap-3 lg:flex-row" onKeyDown={editor.onKeyDown} data-testid="plan-editor">
      <div className="flex min-w-0 flex-1 flex-col gap-2">
        <PlanEditorToolbar editor={editor} hasProblems={hasProblems} />
        {message !== null && (
          <p role="alert" className="text-sm" data-testid="plan-editor-message">
            {message}
          </p>
        )}
        {hasProblems && (
          <ul className="text-sm text-destructive" data-testid="plan-problems">
            {draft.areas
              .filter((a) => problems[a.key] !== undefined)
              .map((a) => (
                <li key={a.key}>
                  {a.name}: {problems[a.key]}
                </li>
              ))}
          </ul>
        )}
        <PlanCanvas
          editor={editor}
          svgRef={svgRef}
          planName={plan.name}
          selectedAreaKey={selectedAreaKey}
          selectedDoorKey={selectedDoorKey}
          backgroundUrl={backgroundUrl}
          backgroundOpacity={backgroundOpacity}
        />
        <p className="text-xs text-muted-foreground">
          Draw: click points, click the first point or press Enter to close. Select: drag points, click a square to add a point, Delete
          removes a point, arrows nudge (Shift ×10), Ctrl+Z / Ctrl+Shift+Z.
        </p>
      </div>
      <PlanSidebar
        editor={editor}
        selectedAreaKey={selectedAreaKey}
        selectedArea={selectedArea}
        selectedDoor={selectedDoor}
        securityLevels={securityLevels}
        searchEmployees={searchEmployees}
      />
    </div>
  );
}
