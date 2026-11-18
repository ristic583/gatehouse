"use client";

import type { LiveDoorEventDto } from "@/lib/api/types";
import { DOOR_EVENT_LABELS, eventColorVar } from "../door-state";

function pad(value: number): string {
  return String(value).padStart(2, "0");
}

export function formatClock(iso: string): string {
  const date = new Date(iso);
  return `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}

/** `HH:mm:ss · person · area · Granted · reader` (spec §6.1); missing parts are skipped. */
export function formatEventRow(event: LiveDoorEventDto): string {
  return [
    formatClock(event.occurredAt),
    event.personDisplayName,
    event.areaName ?? event.doorName,
    DOOR_EVENT_LABELS[event.type],
    event.readerName,
  ]
    .filter((part): part is string => part !== null && part !== "")
    .join(" · ");
}

export interface EventFeedProps {
  events: readonly LiveDoorEventDto[];
}

export function EventFeed({ events }: EventFeedProps) {
  const sorted = [...events].sort((a, b) => b.occurredAt.localeCompare(a.occurredAt));
  return (
    <section aria-label="Live events" className="flex flex-col gap-2">
      <h2 className="text-sm font-semibold">Events</h2>
      {sorted.length === 0 ? <p className="text-sm text-muted-foreground">No events yet.</p> : null}
      <ol className="flex max-h-[32rem] flex-col gap-1 overflow-y-auto text-sm">
        {sorted.map((event) => (
          <li
            key={event.eventId}
            data-testid="live-event"
            data-type={event.type}
            className="border-l-4 pl-2 font-mono"
            style={{ borderColor: `var(${eventColorVar(event.type)})` }}
          >
            {formatEventRow(event)}
          </li>
        ))}
      </ol>
    </section>
  );
}
