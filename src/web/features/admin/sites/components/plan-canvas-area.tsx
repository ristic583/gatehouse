"use client";

import type { DraftArea } from "../draft";
import { mapArea } from "../draft";
import { midpoint, snapPoint } from "../geometry";
import type { PlanEditorState } from "../hooks/use-plan-editor";

/** One area polygon with its label; when selected, vertex handles and "add point" squares on each edge. */
export function PlanCanvasArea({ editor, area, selected }: { editor: PlanEditorState; area: DraftArea; selected: boolean }) {
  const { tool, selection, snap } = editor;
  const invalid = editor.problems[area.key] !== undefined || editor.entityErrors[area.key] !== undefined;
  return (
    <g data-testid={`editor-area-${area.code}`}>
      <polygon
        points={area.polygon.map((p) => `${p.x},${p.y}`).join(" ")}
        fill={invalid ? "var(--destructive)" : "var(--muted)"}
        fillOpacity={selected ? 0.5 : 0.3}
        stroke="currentColor"
        strokeWidth={selected ? 2 : 1}
        onClick={(e) => {
          if (tool !== "select") return;
          e.stopPropagation();
          editor.setSelection({ type: "area", key: area.key });
        }}
      />
      <text
        x={area.polygon.reduce((s, p) => s + p.x, 0) / area.polygon.length}
        y={area.polygon.reduce((s, p) => s + p.y, 0) / area.polygon.length}
        textAnchor="middle"
        fontSize={12}
        pointerEvents="none"
      >
        {area.name}
      </text>
      {selected &&
        area.polygon.map((p, i) => {
          const next = area.polygon[(i + 1) % area.polygon.length]!;
          const mid = midpoint(p, next);
          return (
            <g key={i}>
              <rect
                x={mid.x - 3}
                y={mid.y - 3}
                width={6}
                height={6}
                fill="var(--background)"
                stroke="currentColor"
                data-testid={`add-vertex-${i}`}
                aria-label={`Add point after ${i + 1}`}
                onClick={(e) => {
                  e.stopPropagation();
                  editor.apply((d) =>
                    mapArea(d, area.key, (a) => ({
                      ...a,
                      polygon: [...a.polygon.slice(0, i + 1), snapPoint(mid, snap), ...a.polygon.slice(i + 1)],
                    })),
                  );
                }}
              />
              <circle
                cx={p.x}
                cy={p.y}
                r={5}
                fill={selection?.type === "vertex" && selection.index === i ? "currentColor" : "var(--background)"}
                stroke="currentColor"
                data-testid={`vertex-${i}`}
                onPointerDown={(e) => editor.startDrag({ kind: "vertex", areaKey: area.key, index: i }, e)}
                onClick={(e) => {
                  e.stopPropagation();
                  editor.setSelection({ type: "vertex", areaKey: area.key, index: i });
                }}
              />
            </g>
          );
        })}
    </g>
  );
}
