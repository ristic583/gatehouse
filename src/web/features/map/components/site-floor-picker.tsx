"use client";

import { useTerm } from "@/features/theming/theme";
import type { SiteDto } from "@/lib/api/types";

export interface SiteFloorPickerProps {
  sites: SiteDto[];
  siteId: string;
  floorId: string;
  onChange: (siteId: string, floorId: string) => void;
}

export function SiteFloorPicker({ sites, siteId, floorId, onChange }: SiteFloorPickerProps) {
  const site = sites.find((s) => s.siteId === siteId);
  const siteTerm = useTerm("Site");
  const floorTerm = useTerm("Floor");
  return (
    <div className="flex flex-wrap items-center gap-3 text-sm">
      <label className="flex items-center gap-2">
        {siteTerm}
        <select
          aria-label="Site"
          className="rounded-md border bg-background px-2 py-1"
          value={siteId}
          onChange={(e) => {
            const next = sites.find((s) => s.siteId === e.target.value);
            onChange(e.target.value, next?.floors[0]?.floorId ?? "");
          }}
        >
          {sites.map((s) => (
            <option key={s.siteId} value={s.siteId}>
              {s.name}
            </option>
          ))}
        </select>
      </label>
      <label className="flex items-center gap-2">
        {floorTerm}
        <select
          aria-label="Floor"
          className="rounded-md border bg-background px-2 py-1"
          value={floorId}
          onChange={(e) => onChange(siteId, e.target.value)}
        >
          {(site?.floors ?? []).map((f) => (
            <option key={f.floorId} value={f.floorId}>
              {f.name}
            </option>
          ))}
        </select>
      </label>
    </div>
  );
}
