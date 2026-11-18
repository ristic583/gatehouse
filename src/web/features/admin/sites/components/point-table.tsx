"use client";

import type { PointDto } from "@/lib/api/types";

export interface PointTableProps {
  label: string;
  points: readonly PointDto[];
  onChange: (index: number, point: PointDto) => void;
  onRemove?: (index: number) => void;
  minPoints?: number;
}

/** Accessible alternative to dragging: one row per point, number inputs; ArrowUp/Down ±1 (Shift ±10). */
export function PointTable({ label, points, onChange, onRemove, minPoints = 3 }: PointTableProps) {
  function cell(index: number, axis: "x" | "y") {
    const point = points[index]!;
    const name = `${label} point ${index + 1} ${axis}`;
    return (
      <input
        type="number"
        aria-label={name}
        className="w-20 rounded border px-1 py-0.5"
        value={point[axis]}
        onChange={(event) => {
          const value = Number(event.target.value);
          if (Number.isFinite(value)) onChange(index, { ...point, [axis]: value });
        }}
        onKeyDown={(event) => {
          if (event.key !== "ArrowUp" && event.key !== "ArrowDown") return;
          event.preventDefault();
          const step = (event.shiftKey ? 10 : 1) * (event.key === "ArrowUp" ? 1 : -1);
          onChange(index, { ...point, [axis]: point[axis] + step });
        }}
      />
    );
  }
  return (
    <table className="text-xs" aria-label={`${label} points`} data-testid="point-table">
      <thead>
        <tr>
          <th>#</th>
          <th>X</th>
          <th>Y</th>
          {onRemove && <th />}
        </tr>
      </thead>
      <tbody>
        {points.map((_, i) => (
          <tr key={i}>
            <td>{i + 1}</td>
            <td>{cell(i, "x")}</td>
            <td>{cell(i, "y")}</td>
            {onRemove && (
              <td>
                <button
                  type="button"
                  className="underline"
                  disabled={points.length <= minPoints}
                  onClick={() => onRemove(i)}
                  aria-label={`Remove ${label} point ${i + 1}`}
                >
                  Remove
                </button>
              </td>
            )}
          </tr>
        ))}
      </tbody>
    </table>
  );
}
