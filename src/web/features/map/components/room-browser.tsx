"use client";

import { useState } from "react";
import { DoorOpen, Search } from "lucide-react";

import type { AreaAccessDto } from "@/lib/api/types";
import { ACCESS_STATE_META } from "../access-state";

interface RoomBrowserProps {
  floorName: string;
  areas: readonly AreaAccessDto[];
  selectedAreaId: string | undefined;
  loaded: boolean;
  floorError: boolean;
  onSelect: (area: AreaAccessDto) => void;
}

/** Room list of the current floor with "my spaces" filter and text search. */
export function RoomBrowser({ floorName, areas, selectedAreaId, loaded, floorError, onSelect }: RoomBrowserProps) {
  const [onlyMine, setOnlyMine] = useState(false);
  const [search, setSearch] = useState("");
  const filtered = areas.filter(
    (a) => (!onlyMine || a.state === "HasAccess") && `${a.name} ${a.code}`.toLowerCase().includes(search.toLowerCase()),
  );
  return (
    <section className="gh-panel gh-space-browser">
      <div className="gh-panel-title">
        <span className="gh-panel-kicker">SPACES / {floorName}</span>
        <span>{areas.length} AREAS</span>
      </div>
      <div className="gh-segmented gh-room-filters">
        <button aria-pressed={!onlyMine} onClick={() => setOnlyMine(false)}>
          All spaces
        </button>
        <button aria-pressed={onlyMine} onClick={() => setOnlyMine(true)}>
          My spaces
        </button>
      </div>
      <div className="gh-room-search">
        <Search size={13} />
        <input aria-label="Filter this floor" placeholder="Filter this floor…" value={search} onChange={(e) => setSearch(e.target.value)} />
      </div>
      <div className="gh-room-list">
        {filtered.map((a) => {
          const meta = ACCESS_STATE_META[a.state];
          return (
            <button className="gh-room-row" key={a.areaId} aria-pressed={selectedAreaId === a.areaId} onClick={() => onSelect(a)}>
              <span>
                <DoorOpen size={15} />
              </span>
              <div>
                <strong>{a.name}</strong>
                <small>
                  {a.securityLevelName} · {meta.label}
                </small>
              </div>
              <meta.icon size={14} />
            </button>
          );
        })}
        {!loaded ? (
          <p role="status">{floorError ? "Floor unavailable. Retry loading the floor." : "Loading rooms…"}</p>
        ) : (
          !filtered.length && (
            <p className="gh-detail-note">
              {onlyMine ? "No confirmed access on this floor. Choose All spaces to request access." : "No matching spaces on this floor."}
            </p>
          )
        )}
      </div>
    </section>
  );
}
