"use client";

import type { AreaAccessDto, FloorAccessMapDto } from "@/lib/api/types";
import { AreaShape } from "./area-shape";
import { floorBackgroundUrl, PlanUnderlay, ShowDrawingToggle, useShowDrawing } from "./plan-underlay";

export interface FloorMapProps {
  map: FloorAccessMapDto;
  onAreaSelect: (area: AreaAccessDto) => void;
}

/** Plain SVG floor plan (ADR-007). */
export function FloorMap({ map, onAreaSelect }: FloorMapProps) {
  const [showDrawing, setShowDrawing] = useShowDrawing();
  const hasBackground = map.hasBackground === true;
  return (
    <>
      {hasBackground && <ShowDrawingToggle checked={showDrawing} onChange={setShowDrawing} />}
      <svg
        viewBox={`0 0 ${map.planWidth} ${map.planHeight}`}
        className="h-auto w-full rounded-md border bg-background"
        role="group"
        aria-label={`Access map: ${map.floorName}`}
        data-testid="floor-map"
      >
        <defs>
          <pattern id="hatch-pending" width={12} height={12} patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
            <line x1={0} y1={0} x2={0} y2={12} stroke="var(--gh-state-pending)" strokeWidth={4} strokeOpacity={0.5} />
          </pattern>
        </defs>
        {hasBackground && showDrawing && (
          <PlanUnderlay url={floorBackgroundUrl(map.floorId, map.backgroundUpdatedAt)} width={map.planWidth} height={map.planHeight} />
        )}
        {map.areas.map((area) => (
          <AreaShape key={area.areaId} area={area} onSelect={onAreaSelect} />
        ))}
        {map.areas.flatMap((area) =>
          area.doors.map((door) => (
            <rect key={door.doorId} x={door.x - 8} y={door.y - 3} width={16} height={6} fill="currentColor" aria-hidden="true">
              <title>{door.name}</title>
            </rect>
          )),
        )}
      </svg>
    </>
  );
}
