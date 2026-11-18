"use client";

import { motion } from "framer-motion";
import type { LiveDoorDto, LiveDoorEventDto, LiveFloorDto } from "@/lib/api/types";
import { floorBackgroundUrl, PlanUnderlay, ShowDrawingToggle, useShowDrawing } from "@/features/map/components/plan-underlay";
import { DoorMarker } from "./door-marker";

export interface LiveFloorPlanProps {
  floor: LiveFloorDto;
  onDoorSelect?: (door: LiveDoorDto) => void;
}

function newestByDoor(events: readonly LiveDoorEventDto[]): Map<string, LiveDoorEventDto> {
  const result = new Map<string, LiveDoorEventDto>();
  for (const event of events) {
    if (event.doorId !== null && !result.has(event.doorId)) {
      result.set(event.doorId, event);
    }
  }
  return result;
}

/** Plain SVG live floor plan (ADR-007): areas, doors with state, readers as dots. */
export function LiveFloorPlan({ floor, onDoorSelect }: LiveFloorPlanProps) {
  const lastEvents = newestByDoor(floor.recentEvents);
  const [showDrawing, setShowDrawing] = useShowDrawing();
  const hasBackground = floor.hasBackground === true;

  return (
    <>
      {hasBackground && <ShowDrawingToggle checked={showDrawing} onChange={setShowDrawing} />}
      <svg
        viewBox={`0 0 ${floor.planWidth} ${floor.planHeight}`}
        className="h-auto w-full rounded-md border bg-background"
        role="group"
        aria-label={`Live floor plan: ${floor.floorName}`}
        data-testid="live-floor-plan"
      >
        {hasBackground && showDrawing && (
          <PlanUnderlay
            url={floorBackgroundUrl(floor.floorId, floor.backgroundUpdatedAt)}
            width={floor.planWidth}
            height={floor.planHeight}
          />
        )}
        {floor.areas.map((area) => (
          <g key={area.areaId} data-testid={`live-area-${area.code}`}>
            <polygon
              points={area.polygon.map((p) => `${p.x},${p.y}`).join(" ")}
              fill="var(--muted)"
              fillOpacity={0.4}
              stroke="currentColor"
              strokeOpacity={0.4}
            />
            <text
              x={area.polygon.reduce((s, p) => s + p.x, 0) / Math.max(area.polygon.length, 1)}
              y={area.polygon.reduce((s, p) => s + p.y, 0) / Math.max(area.polygon.length, 1)}
              textAnchor="middle"
              fontSize={13}
              fill="currentColor"
              fillOpacity={0.7}
            >
              {area.name}
            </text>
            <text
              data-testid={`occupancy-${area.code}`}
              x={area.polygon.reduce((s, p) => s + p.x, 0) / Math.max(area.polygon.length, 1)}
              y={area.polygon.reduce((s, p) => s + p.y, 0) / Math.max(area.polygon.length, 1) + 18}
              textAnchor="middle"
              fontSize={12}
              fill="currentColor"
            >
              <title>Approximate occupancy (in/out readers)</title>
              {`≈ ${floor.occupancy?.find((o) => o.areaId === area.areaId)?.count ?? 0} inside`}
            </text>
          </g>
        ))}
        {floor.doors.flatMap((door) => {
          const last = lastEvents.get(door.doorId) ?? null;
          return door.readers.map((reader) => {
            const swiped = last !== null && last.readerName === reader.name;
            return (
              <g key={reader.readerId} transform={`translate(${reader.position.x} ${reader.position.y})`}>
                <title>{reader.name}</title>
                {swiped ? (
                  <motion.circle
                    key={last.eventId}
                    r={4}
                    fill="none"
                    stroke="currentColor"
                    initial={{ scale: 1, opacity: 0.9 }}
                    animate={{ scale: 3, opacity: 0 }}
                    transition={{ duration: 1 }}
                  />
                ) : null}
                <circle r={3.5} fill="currentColor" data-testid="live-reader" />
              </g>
            );
          });
        })}
        {floor.doors.map((door) => (
          <DoorMarker key={door.doorId} door={door} lastEvent={lastEvents.get(door.doorId) ?? null} onSelect={onDoorSelect} />
        ))}
      </svg>
    </>
  );
}
