"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import type { AreaRosterEntryDto } from "@/lib/api/types";
import { rosterApi } from "../api";
import { RevokeDialog, type RevokeTarget } from "./revoke-dialog";

export function rosterStatusText(entry: AreaRosterEntryDto): string {
  if (entry.status === "Revoked") {
    return "Revocation pending";
  }
  return entry.fulfillmentStatus === "Confirmed" ? "Active" : "Approved - not yet applied";
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString();
}

export function AreaRoster({ areaId }: { areaId: string }) {
  const query = useQuery({ queryKey: ["area-roster", areaId], queryFn: () => rosterApi.roster(areaId) });
  const [target, setTarget] = useState<RevokeTarget | null>(null);

  if (query.isError) {
    return <p role="alert">Could not load the roster.</p>;
  }
  if (query.data === undefined) {
    return <p>Loading…</p>;
  }
  const { area, entries, canRevoke, canEmergencyRevoke } = query.data;
  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-xl font-semibold">
        {area.name} · {area.siteName} · {area.floorName}
      </h1>
      {entries.length === 0 ? <p>No one has access to this area.</p> : null}
      <table className="w-full text-left text-sm">
        <thead>
          <tr>
            <th>Person</th>
            <th>Type</th>
            <th>Window</th>
            <th>Status</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {entries.map((e) => (
            <tr key={e.entitlementId} data-testid={`roster-${e.entitlementId}`} className="border-t">
              <td>{e.subject.displayName}</td>
              <td>{e.personType}</td>
              <td>
                {formatDate(e.requestedStart)} – {formatDate(e.effectiveEnd)}
              </td>
              <td>
                <Badge variant="secondary">{rosterStatusText(e)}</Badge>
              </td>
              <td className="flex gap-2">
                {canRevoke && e.status !== "Revoked" ? (
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => setTarget({ entitlementId: e.entitlementId, personName: e.subject.displayName, emergency: false })}
                  >
                    Revoke
                  </Button>
                ) : null}
                {canEmergencyRevoke && e.status !== "Revoked" ? (
                  <Button
                    size="sm"
                    variant="destructive"
                    onClick={() => setTarget({ entitlementId: e.entitlementId, personName: e.subject.displayName, emergency: true })}
                  >
                    Emergency revoke
                  </Button>
                ) : null}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <RevokeDialog target={target} onClose={() => setTarget(null)} />
    </div>
  );
}
