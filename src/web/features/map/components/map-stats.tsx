"use client";

import { Building2, Clock3, Layers3, ShieldCheck } from "lucide-react";

import type { AreaAccessDto, SiteDto } from "@/lib/api/types";

interface MapStatsProps {
  site: SiteDto;
  floorName: string;
  areas: readonly AreaAccessDto[];
  loaded: boolean;
}

/** Headline counts of the selected site and floor. */
export function MapStats({ site, floorName, areas, loaded }: MapStatsProps) {
  const stats = [
    { label: "BUILDING LEVELS", value: site.floors.length, note: site.code, icon: Building2 },
    { label: "CONTROLLED SPACES", value: areas.length, note: floorName, icon: Layers3 },
    {
      label: "CONFIRMED ACCESS",
      value: areas.filter((a) => a.state === "HasAccess").length,
      note: "Personal access · this floor",
      icon: ShieldCheck,
    },
    {
      label: "PENDING ACCESS",
      value: areas.filter((a) => a.state === "Pending").length,
      note: "Approval or application in progress",
      icon: Clock3,
    },
  ];
  return (
    <div className="gh-stat-row">
      {stats.map(({ label, value, note, icon: Icon }, i) => (
        <div className="gh-stat" key={label}>
          <div>
            <p className="gh-stat-label">
              <span>0{i + 1}</span>
              {label}
            </p>
            <strong>{loaded ? value : "—"}</strong>
            <small>{note}</small>
          </div>
          <span className="gh-stat-icon">
            <Icon size={18} />
          </span>
        </div>
      ))}
    </div>
  );
}
