"use client";

import { motion } from "framer-motion";
import type { LiveDoorDto, LiveDoorEventDto } from "@/lib/api/types";
import { DOOR_STATE_META } from "../door-state";

export interface DoorMarkerProps {
  door: LiveDoorDto;
  /** Newest event at this door; animations replay when its id changes. */
  lastEvent: LiveDoorEventDto | null;
  /** Set for staff who may act; only connector-managed doors become clickable. */
  onSelect?: (door: LiveDoorDto) => void;
}

const SHAKE = { x: [0, -4, 4, -3, 3, 0] };

export function DoorMarker({ door, lastEvent, onSelect }: DoorMarkerProps) {
  const meta = DOOR_STATE_META[door.state];
  const Icon = meta.icon;
  const color = `var(${meta.colorVar})`;
  const ref = door.pacsDoorRef ?? door.doorId;
  const eventKey = lastEvent?.eventId ?? "none";
  const denied = lastEvent?.type === "AccessDenied";
  const granted = lastEvent?.type === "AccessGranted";
  const opened = lastEvent?.type === "DoorOpened";
  const select = door.connectorManaged && onSelect !== undefined ? () => onSelect(door) : undefined;

  return (
    <g
      transform={`translate(${door.position.x} ${door.position.y})`}
      data-testid={`door-${ref}`}
      data-state={door.state}
      role={select !== undefined ? "button" : undefined}
      tabIndex={select !== undefined ? 0 : undefined}
      aria-label={select !== undefined ? `${door.name}: ${meta.label}` : undefined}
      className={select !== undefined ? "cursor-pointer" : undefined}
      onClick={select}
      onKeyDown={
        select !== undefined
          ? (event) => {
              if (event.key === "Enter" || event.key === " ") {
                event.preventDefault();
                select();
              }
            }
          : undefined
      }
    >
      <title>{`${door.name}: ${meta.label}`}</title>
      {granted ? (
        <motion.circle
          key={`flash-${eventKey}`}
          r={18}
          fill="var(--gh-door-unlocked)"
          initial={{ opacity: 0.7 }}
          animate={{ opacity: 0 }}
          transition={{ duration: 1.2 }}
        />
      ) : null}
      <motion.g key={`body-${eventKey}`} animate={denied ? SHAKE : { x: 0 }} transition={{ duration: 0.5 }}>
        <motion.g animate={opened ? { rotate: [0, -25, 0] } : { rotate: 0 }} transition={{ duration: 0.8 }}>
          <rect x={-11} y={-11} width={22} height={22} rx={4} fill="var(--background)" stroke={color} strokeWidth={2} />
          <Icon x={-8} y={-8} width={16} height={16} color={color} aria-hidden="true" />
        </motion.g>
      </motion.g>
      <text y={24} textAnchor="middle" fontSize={11} fill={color} data-testid="door-state-label">
        {meta.label}
      </text>
      {meta.alarm ? (
        <motion.circle
          cx={12}
          cy={-12}
          r={5}
          fill="var(--gh-door-forced-open)"
          data-testid="door-alarm-badge"
          animate={{ opacity: [1, 0.2, 1] }}
          transition={{ duration: 1, repeat: Infinity }}
        />
      ) : null}
    </g>
  );
}
