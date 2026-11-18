"use client";

import { Box, Building2, LayoutGrid } from "lucide-react";
import type { SiteDto } from "@/lib/api/types";
import type { MapMode } from "../hooks/use-map-preferences";

interface AtlasToolbarProps {
  site: SiteDto;
  siteList: readonly SiteDto[];
  floor: SiteDto["floors"][number];
  mode: MapMode;
  onMode: (mode: MapMode) => void;
  onNavigate: (siteId: string, floorId: string) => void;
}

/** Site picker, view-mode switch (list / 3D / plan) and floor picker above the map. */
export function AtlasToolbar({ site, siteList, floor, mode, onMode, onNavigate }: AtlasToolbarProps) {
  return (
    <>
      <div className="gh-atlas-toolbar">
        <div>
          <label>
            <Building2 size={17} />
            <select
              aria-label="Site"
              value={site.siteId}
              onChange={(e) => {
                const next = siteList.find((s) => s.siteId === e.target.value);
                if (next) onNavigate(next.siteId, next.floors[0]?.floorId ?? "");
              }}
            >
              {siteList.map((s) => (
                <option key={s.siteId} value={s.siteId}>
                  {s.name}
                </option>
              ))}
            </select>
          </label>
          <small>
            {site.code} <span>/</span> SPATIAL ACCESS MODEL
          </small>
        </div>
        <div className="gh-segmented">
          <button aria-pressed={mode === "list"} onClick={() => onMode("list")}>
            Room list
          </button>
          <button aria-pressed={mode === "spatial"} onClick={() => onMode("spatial")}>
            <Box size={13} /> 3D model
          </button>
          <button aria-pressed={mode === "plan"} onClick={() => onMode("plan")}>
            <LayoutGrid size={13} /> Floor plan
          </button>
        </div>
      </div>
      <label className="gh-floor-picker">
        Floor
        <select aria-label="Floor" value={floor.floorId} onChange={(e) => onNavigate(site.siteId, e.target.value)}>
          {[...site.floors]
            .sort((a, b) => a.level - b.level)
            .map((f) => (
              <option key={f.floorId} value={f.floorId}>
                {f.name}
              </option>
            ))}
        </select>
        <span>{mode === "spatial" ? "Single floor first. Explore other levels when needed." : "Choose a floor, then select a room."}</span>
      </label>
    </>
  );
}
