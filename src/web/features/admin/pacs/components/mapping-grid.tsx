"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import type { AccessLevelMappingDto, AccessLevelMappingRequest } from "@/lib/api/types";

export interface MappableArea {
  areaId: string;
  name: string;
  siteName: string;
  floorName: string;
}

/** Every area on the instance's sites with its vendor access-level reference; empty = unmapped. */
export function MappingGrid({
  areas,
  mappings,
  onSave,
}: {
  areas: readonly MappableArea[];
  mappings: readonly AccessLevelMappingDto[];
  onSave: (body: AccessLevelMappingRequest[]) => Promise<unknown>;
}) {
  const [refs, setRefs] = useState<Record<string, string>>(() =>
    Object.fromEntries(mappings.map((m) => [m.areaId, m.vendorAccessLevelRef])),
  );
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const unmapped = areas.filter((a) => (refs[a.areaId] ?? "").trim() === "").length;

  async function save() {
    setBusy(true);
    setError(null);
    try {
      await onSave(
        areas
          .filter((a) => (refs[a.areaId] ?? "").trim() !== "")
          .map((a) => ({
            areaId: a.areaId,
            vendorAccessLevelRef: (refs[a.areaId] ?? "").trim(),
            displayName: mappings.find((m) => m.areaId === a.areaId)?.displayName ?? null,
          })),
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : "The mappings could not be saved.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="gh-panel flex flex-col gap-3" aria-label="Access-level mappings">
      <div className="flex items-center justify-between">
        <h3 className="gh-panel-title">Access-level mappings</h3>
        <span className={`text-sm ${unmapped > 0 ? "text-destructive" : "text-muted-foreground"}`} data-testid="unmapped-count">
          {unmapped} unmapped
        </span>
      </div>
      {areas.length === 0 ? (
        <p className="text-sm text-muted-foreground">No areas on this instance&apos;s sites yet.</p>
      ) : (
        <div className="gh-table-wrap">
          <table className="w-full text-left">
            <thead>
              <tr>
                <th>Area</th>
                <th>Location</th>
                <th>Vendor access level</th>
              </tr>
            </thead>
            <tbody>
              {areas.map((a) => {
                const value = refs[a.areaId] ?? "";
                return (
                  <tr key={a.areaId} data-testid="mapping-row" data-mapped={value.trim() !== ""}>
                    <td>{a.name}</td>
                    <td>
                      {a.siteName} · {a.floorName}
                    </td>
                    <td>
                      <input
                        aria-label={`Access level for ${a.name}`}
                        className="w-full rounded border px-2 py-1"
                        value={value}
                        placeholder="Unmapped"
                        onChange={(e) => setRefs((r) => ({ ...r, [a.areaId]: e.target.value }))}
                      />
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      {error !== null && (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}
      <Button type="button" className="self-start" onClick={() => void save()} disabled={busy}>
        Save mappings
      </Button>
    </section>
  );
}
