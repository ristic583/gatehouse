import { type RefObject, useCallback, useMemo, useRef, useState, type KeyboardEvent, type MouseEvent, type PointerEvent } from "react";
import type { FloorPlanDto, PointDto, SecurityLevelAdminDto } from "@/lib/api/types";
import { commit, type Draft, type DraftArea, draftFromPlan, type History, mapArea, mapDoor, newKey, redo, undo } from "../draft";
import { clampToPlan, polygonProblem, PROBLEM_TEXT, snapPoint } from "../geometry";
import type { SaveOutcome } from "../save";
import {
  CLOSE_DISTANCE,
  type DragTarget,
  doorPlacement,
  dragTo,
  type EditorTool,
  nudgeSelection,
  round2,
  type Selection,
  sharedWallAreas,
} from "../plan-editor-ops";

const ARROWS: Record<string, [number, number]> = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };

/** State and actions of the floor-plan editor: draft with a 50-step undo history, tool, selection, drawing and save. */
export function usePlanEditor(
  plan: FloorPlanDto,
  securityLevels: readonly SecurityLevelAdminDto[],
  onSave: (original: Draft, current: Draft) => Promise<SaveOutcome>,
  svgRef: RefObject<SVGSVGElement | null>,
) {
  const W = plan.planWidth;
  const H = plan.planHeight;
  const [original, setOriginal] = useState<Draft>(() => draftFromPlan(plan));
  const [history, setHistory] = useState<History>(() => ({ past: [], present: draftFromPlan(plan), future: [] }));
  const [tool, setTool] = useState<EditorTool>("select");
  const [snap, setSnap] = useState(true);
  const [selection, setSelection] = useState<Selection>(null);
  const [drawing, setDrawing] = useState<PointDto[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [entityErrors, setEntityErrors] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const dragging = useRef<DragTarget | null>(null);

  const draft = history.present;
  const apply = useCallback((fn: (d: Draft) => Draft) => setHistory((h) => commit(h, fn(h.present))), []);

  const problems = useMemo(() => {
    const result: Record<string, string> = {};
    for (const area of draft.areas) {
      const others = draft.areas.filter((o) => o.key !== area.key).map((o) => o.polygon);
      const problem = polygonProblem(area.polygon, W, H, others);
      if (problem !== null) result[area.key] = PROBLEM_TEXT[problem];
    }
    return result;
  }, [draft, W, H]);

  function toPlan(event: { clientX: number; clientY: number }): PointDto {
    const rect = svgRef.current?.getBoundingClientRect();
    if (rect === undefined || rect.width === 0 || rect.height === 0) return { x: event.clientX, y: event.clientY };
    return { x: ((event.clientX - rect.left) * W) / rect.width, y: ((event.clientY - rect.top) * H) / rect.height };
  }

  function finishArea(points: PointDto[]) {
    const problem = polygonProblem(
      points,
      W,
      H,
      draft.areas.map((a) => a.polygon),
    );
    setDrawing([]);
    if (problem !== null) {
      setMessage(PROBLEM_TEXT[problem]);
      return;
    }
    const n = draft.areas.length + 1;
    const area: DraftArea = {
      key: newKey("area"),
      id: null,
      name: `Area ${n}`,
      code: `AREA-${n}`,
      polygon: points,
      securityLevelId: securityLevels[0]?.id ?? "",
      owners: [],
      doors: [],
    };
    apply((d) => ({ areas: [...d.areas, area] }));
    setSelection({ type: "area", key: area.key });
    setTool("select");
    setMessage(null);
  }

  function placeDoor(click: PointDto) {
    const target = doorPlacement(draft.areas, click);
    if (target === null) {
      setMessage("Draw an area before placing doors.");
      return;
    }
    const shared = sharedWallAreas(draft.areas, target.area.key, target.point);
    const count = draft.areas.reduce((s, a) => s + a.doors.length, 0) + 1;
    const door = { key: newKey("door"), id: null, name: `Door ${count}`, position: round2(target.point), pacsDoorRef: "", readers: [] };
    apply((d) => mapArea(d, target.area.key, (a) => ({ ...a, doors: [...a.doors, door] })));
    setSelection({ type: "door", key: door.key });
    setMessage(
      shared.length > 0
        ? `Door placed on ${target.area.name}; the wall is shared with ${shared.join(", ")}. Change its area in the inspector if needed.`
        : null,
    );
  }

  function placeReader(click: PointDto) {
    const doors = draft.areas.flatMap((a) => a.doors);
    const selectedDoor = selection?.type === "door" ? doors.find((d) => d.key === selection.key) : undefined;
    const distance = (p: PointDto) => Math.hypot(p.x - click.x, p.y - click.y);
    const door = selectedDoor ?? [...doors].sort((a, b) => distance(a.position) - distance(b.position))[0];
    if (door === undefined) {
      setMessage("Place a door before adding readers.");
      return;
    }
    const reader = {
      key: newKey("reader"),
      id: null,
      name: `Reader ${door.readers.length + 1}`,
      direction: "In" as const,
      position: snapPoint(clampToPlan(click, W, H), snap),
      pacsReaderRef: "",
    };
    apply((d) => mapDoor(d, door.key, (x) => ({ ...x, readers: [...x.readers, reader] })));
    setSelection({ type: "door", key: door.key });
    setMessage(null);
  }

  function onCanvasClick(event: MouseEvent<SVGSVGElement>) {
    const click = toPlan(event);
    if (tool === "area") {
      const point = snapPoint(clampToPlan(click, W, H), snap);
      const first = drawing[0];
      if (first !== undefined && drawing.length >= 3 && Math.hypot(first.x - point.x, first.y - point.y) <= CLOSE_DISTANCE) {
        finishArea(drawing);
      } else {
        setDrawing((pts) => [...pts, point]);
      }
    } else if (tool === "door") {
      placeDoor(click);
    } else if (tool === "reader") {
      placeReader(click);
    } else {
      setSelection(null);
    }
  }

  function onPointerMove(event: PointerEvent<SVGSVGElement>) {
    const drag = dragging.current;
    if (drag === null) return;
    const point = snapPoint(clampToPlan(toPlan(event), W, H), snap);
    setHistory((h) => ({ ...h, present: dragTo(h.present, drag, point) }));
  }

  function startDrag(target: DragTarget, event: PointerEvent) {
    if (tool !== "select") return;
    event.stopPropagation();
    dragging.current = target;
    // Snapshot once per drag so a whole drag is one undo step.
    setHistory((h) => ({ past: [...h.past, h.present].slice(-50), present: h.present, future: [] }));
  }

  function endDrag() {
    dragging.current = null;
  }

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    const target = event.target as HTMLElement;
    if (target.tagName === "INPUT" || target.tagName === "TEXTAREA" || target.tagName === "SELECT") return;
    const mod = event.metaKey || event.ctrlKey;
    if (mod && event.key.toLowerCase() === "z") {
      event.preventDefault();
      setHistory((h) => (event.shiftKey ? redo(h) : undo(h)));
      return;
    }
    if (mod && event.key.toLowerCase() === "y") {
      event.preventDefault();
      setHistory(redo);
      return;
    }
    if (tool === "area" && event.key === "Enter" && drawing.length >= 3) {
      finishArea(drawing);
      return;
    }
    if (event.key === "Escape") {
      setDrawing([]);
      setSelection(null);
      return;
    }
    const arrow = ARROWS[event.key];
    if (arrow !== undefined && selection !== null) {
      event.preventDefault();
      const step = event.shiftKey ? 10 : 1;
      const move = (p: PointDto) => clampToPlan({ x: p.x + arrow[0] * step, y: p.y + arrow[1] * step }, W, H);
      apply((d) => nudgeSelection(d, selection, move));
      return;
    }
    if ((event.key === "Delete" || event.key === "Backspace") && selection?.type === "vertex") {
      event.preventDefault();
      const area = draft.areas.find((a) => a.key === selection.areaKey);
      if (area === undefined) return;
      if (area.polygon.length <= 3) {
        setMessage("An area needs at least 3 points.");
        return;
      }
      apply((d) => mapArea(d, selection.areaKey, (a) => ({ ...a, polygon: a.polygon.filter((_, i) => i !== selection.index) })));
      setSelection({ type: "area", key: selection.areaKey });
    }
  }

  async function save() {
    setSaving(true);
    setMessage(null);
    try {
      const outcome = await onSave(original, draft);
      setEntityErrors(outcome.errors);
      setOriginal(outcome.original);
      setHistory((h) => ({ ...h, present: outcome.current }));
      setMessage(Object.keys(outcome.errors).length === 0 ? "Saved." : "Some changes were not saved. See the marked items.");
    } finally {
      setSaving(false);
    }
  }

  function pickTool(next: EditorTool) {
    setTool(next);
    setDrawing([]);
  }

  return {
    W,
    H,
    draft,
    history,
    setHistory,
    apply,
    tool,
    pickTool,
    snap,
    setSnap,
    selection,
    setSelection,
    drawing,
    message,
    entityErrors,
    saving,
    problems,
    finishArea,
    placeReader,
    onCanvasClick,
    onPointerMove,
    startDrag,
    endDrag,
    onKeyDown,
    save,
  };
}

export type PlanEditorState = ReturnType<typeof usePlanEditor>;
