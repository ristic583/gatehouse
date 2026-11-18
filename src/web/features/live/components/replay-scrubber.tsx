"use client";

import { useQuery } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import type { LiveFloorDto } from "@/lib/api/types";
import { liveApi } from "../api";
import { floorAt } from "../replay";
import { LiveFloorPlan } from "./live-floor-plan";

const WINDOW_MINUTES = 24 * 60;

/** Replays the last 24 h of door events of a floor; the slider picks the moment (minutes before the replay started). */
export function ReplayScrubber({ floor }: { floor: LiveFloorDto }) {
  const [anchor] = useState(() => Date.now());
  const [minutesAgo, setMinutesAgo] = useState(60);
  const events = useQuery({
    queryKey: ["live-replay", floor.floorId, anchor],
    queryFn: () =>
      liveApi.floorEvents(floor.floorId, new Date(anchor - WINDOW_MINUTES * 60_000).toISOString(), new Date(anchor).toISOString()),
    staleTime: Infinity,
  });
  const at = anchor - minutesAgo * 60_000;
  const replayed = useMemo(() => (events.data === undefined ? undefined : floorAt(floor, events.data, at)), [events.data, floor, at]);

  return (
    <div className="flex flex-col gap-3" data-testid="replay">
      <label className="flex flex-wrap items-center gap-3 text-sm">
        Replay time
        <input
          type="range"
          aria-label="Replay time"
          min={0}
          max={WINDOW_MINUTES}
          step={1}
          value={WINDOW_MINUTES - minutesAgo}
          onChange={(e) => setMinutesAgo(WINDOW_MINUTES - Number(e.target.value))}
          className="w-80"
        />
        <span data-testid="replay-time">{new Date(at).toLocaleString()}</span>
      </label>
      <p className="text-xs text-muted-foreground">
        Door states are rebuilt from the events in the last 24 h (doors start as locked at the window start).
      </p>
      {events.isError ? <p role="alert">Could not load the door events.</p> : null}
      {replayed === undefined ? events.isError ? null : <p>Loading events…</p> : <LiveFloorPlan floor={replayed} />}
    </div>
  );
}
