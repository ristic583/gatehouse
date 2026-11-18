"use client";

import type { KeyboardEvent } from "react";
import type { AreaAccessDto } from "@/lib/api/types";
import { ACCESS_STATE_META, CARD_INACTIVE_NOTE } from "../access-state";

export interface AreaShapeProps {
  area: AreaAccessDto;
  onSelect: (area: AreaAccessDto) => void;
}

function centroid(area: AreaAccessDto): { x: number; y: number } {
  const n = area.polygon.length || 1;
  return {
    x: area.polygon.reduce((sum, p) => sum + p.x, 0) / n,
    y: area.polygon.reduce((sum, p) => sum + p.y, 0) / n,
  };
}

export function AreaShape({ area, onSelect }: AreaShapeProps) {
  const meta = ACCESS_STATE_META[area.state];
  const Icon = meta.icon;
  const color = `var(${meta.colorVar})`;
  const center = centroid(area);
  const points = area.polygon.map((p) => `${p.x},${p.y}`).join(" ");

  const onKeyDown = (event: KeyboardEvent<SVGGElement>) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      onSelect(area);
    }
  };

  return (
    <g
      role="button"
      tabIndex={0}
      aria-label={`${area.name}: ${meta.label}`}
      data-testid={`area-${area.code}`}
      data-state={area.state}
      className="cursor-pointer focus:outline-none [&:focus-visible>polygon]:stroke-[4]"
      onClick={() => onSelect(area)}
      onKeyDown={onKeyDown}
    >
      <polygon points={points} fill={color} fillOpacity={0.25} stroke={color} strokeWidth={2} />
      {meta.hatched ? <polygon points={points} fill="url(#hatch-pending)" data-testid={`hatch-${area.code}`} /> : null}
      <Icon x={center.x - 12} y={center.y - 44} width={24} height={24} color={color} aria-hidden="true" />
      <text x={center.x} y={center.y} textAnchor="middle" fontSize={18} fontWeight={600} fill="currentColor">
        {area.name}
      </text>
      <text x={center.x} y={center.y + 22} textAnchor="middle" fontSize={15} fill={color}>
        {meta.label}
      </text>
      {area.cardInactive ? (
        <text x={center.x} y={center.y + 42} textAnchor="middle" fontSize={12} fill="currentColor">
          {CARD_INACTIVE_NOTE}
        </text>
      ) : null}
    </g>
  );
}
