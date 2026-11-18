"use client";

import type { RefObject } from "react";
import type { PlanEditorState } from "../hooks/use-plan-editor";
import { PlanCanvasArea } from "./plan-canvas-area";

interface PlanCanvasProps {
  editor: PlanEditorState;
  svgRef: RefObject<SVGSVGElement | null>;
  planName: string;
  selectedAreaKey: string | null;
  selectedDoorKey: string | null;
  backgroundUrl?: string | null;
  backgroundOpacity: number;
}

/** The SVG drawing surface: background, grid, areas, doors, readers and the polygon being drawn. */
export function PlanCanvas({
  editor,
  svgRef,
  planName,
  selectedAreaKey,
  selectedDoorKey,
  backgroundUrl,
  backgroundOpacity,
}: PlanCanvasProps) {
  const { W, H, draft, tool, drawing, snap, entityErrors } = editor;
  return (
    <svg
      ref={svgRef}
      viewBox={`0 0 ${W} ${H}`}
      className="h-auto w-full touch-none rounded-md border bg-background"
      role="application"
      aria-label={`Floor plan editor: ${planName}`}
      tabIndex={0}
      data-testid="plan-editor-canvas"
      onClick={editor.onCanvasClick}
      onPointerMove={editor.onPointerMove}
      onPointerUp={editor.endDrag}
      onPointerLeave={editor.endDrag}
    >
      {backgroundUrl && (
        <image
          href={backgroundUrl}
          x={0}
          y={0}
          width={W}
          height={H}
          preserveAspectRatio="none"
          opacity={backgroundOpacity}
          data-testid="editor-background"
        />
      )}
      {snap && (
        <pattern id="editor-grid" width={50} height={50} patternUnits="userSpaceOnUse">
          <path d="M 50 0 L 0 0 0 50" fill="none" stroke="currentColor" strokeOpacity={0.08} />
        </pattern>
      )}
      {snap && <rect width={W} height={H} fill="url(#editor-grid)" pointerEvents="none" />}
      {draft.areas.map((area) => (
        <PlanCanvasArea key={area.key} editor={editor} area={area} selected={area.key === selectedAreaKey} />
      ))}
      {draft.areas.flatMap((area) =>
        area.doors.map((door) => (
          <g key={door.key}>
            <rect
              x={door.position.x - 7}
              y={door.position.y - 4}
              width={14}
              height={8}
              fill={entityErrors[door.key] !== undefined ? "var(--destructive)" : "currentColor"}
              stroke={door.key === selectedDoorKey ? "var(--primary)" : "none"}
              strokeWidth={2}
              data-testid={`editor-door-${door.name}`}
              data-x={door.position.x}
              data-y={door.position.y}
              onPointerDown={(e) => editor.startDrag({ kind: "door", key: door.key }, e)}
              onClick={(e) => {
                if (tool !== "select") return;
                e.stopPropagation();
                editor.setSelection({ type: "door", key: door.key });
              }}
            >
              <title>{door.name}</title>
            </rect>
            {door.readers.map((reader) => (
              <circle
                key={reader.key}
                cx={reader.position.x}
                cy={reader.position.y}
                r={4}
                fill={reader.direction === "In" ? "var(--primary)" : "var(--muted-foreground)"}
                data-testid={`editor-reader-${reader.name}`}
                onClick={(e) => {
                  if (tool !== "select") return;
                  e.stopPropagation();
                  editor.setSelection({ type: "reader", doorKey: door.key, key: reader.key });
                }}
              >
                <title>{reader.name}</title>
              </circle>
            ))}
          </g>
        )),
      )}
      {drawing.length > 0 && (
        <g data-testid="drawing">
          <polyline points={drawing.map((p) => `${p.x},${p.y}`).join(" ")} fill="none" stroke="currentColor" strokeDasharray="4 3" />
          {drawing.map((p, i) => (
            <circle key={i} cx={p.x} cy={p.y} r={3} fill="currentColor" />
          ))}
        </g>
      )}
    </svg>
  );
}
