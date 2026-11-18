"use client";
import { useRef, useState } from "react";
import { Search, X } from "lucide-react";
import type { AreaAccessDto, FloorAccessMapDto } from "@/lib/api/types";
import { ACCESS_STATE_META } from "../access-state";
export function BuildingSearch({
  maps,
  loading,
  incomplete,
  onSelect,
}: {
  maps: FloorAccessMapDto[];
  loading: boolean;
  incomplete: boolean;
  onSelect: (map: FloorAccessMapDto, area: AreaAccessDto) => void;
}) {
  const [query, setQuery] = useState("");
  const root = useRef<HTMLDivElement>(null);
  const input = useRef<HTMLInputElement>(null);
  const words = query.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean);
  const results = words.length
    ? maps.flatMap((map) =>
        map.areas
          .filter((area) =>
            words.every((w) => `${area.name} ${area.code} ${area.securityLevelName} ${map.floorName}`.toLocaleLowerCase().includes(w)),
          )
          .map((area) => ({ map, area })),
      )
    : [];
  return (
    <div
      className="gh-building-search"
      ref={root}
      onKeyDown={(e) => {
        if (e.key === "Escape") {
          setQuery("");
          input.current?.focus();
        }
        if (e.key !== "ArrowDown" && e.key !== "ArrowUp") return;
        const buttons = Array.from(root.current?.querySelectorAll<HTMLButtonElement>(".gh-search-results button") ?? []);
        if (!buttons.length) return;
        e.preventDefault();
        const at = buttons.indexOf(document.activeElement as HTMLButtonElement);
        const next = e.key === "ArrowDown" ? Math.min(at + 1, buttons.length - 1) : at - 1;
        if (next < 0) input.current?.focus();
        else buttons[next]?.focus();
      }}
    >
      <label htmlFor="building-room-search">
        <Search size={17} />
        <input
          ref={input}
          id="building-room-search"
          type="search"
          autoComplete="off"
          placeholder="Find any room in this building…"
          aria-label="Search this building"
          aria-controls={words.length ? "building-search-results" : undefined}
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
      </label>
      {query && (
        <button
          className="gh-search-clear"
          aria-label="Clear building search"
          onClick={() => {
            setQuery("");
            input.current?.focus();
          }}
        >
          <X size={15} />
        </button>
      )}
      {words.length > 0 && (
        <div className="gh-search-results" id="building-search-results">
          <p role="status">
            {results.length} {results.length === 1 ? "space" : "spaces"} found across all floors{loading ? " · Still loading floors…" : ""}
            {incomplete ? " · Some floors unavailable" : ""}
          </p>
          <ul>
            {results.map(({ map, area }) => {
              const meta = ACCESS_STATE_META[area.state];
              return (
                <li key={area.areaId}>
                  <button
                    onClick={() => {
                      setQuery("");
                      onSelect(map, area);
                    }}
                  >
                    <meta.icon size={16} />
                    <span>
                      <strong>{area.name}</strong>
                      <small>
                        {map.floorName} · {area.code} · {meta.label}
                      </small>
                    </span>
                    <span>View room →</span>
                  </button>
                </li>
              );
            })}
          </ul>
          {!results.length && !loading && <p>Try a room name, area code, security level, or floor.</p>}
        </div>
      )}
    </div>
  );
}
