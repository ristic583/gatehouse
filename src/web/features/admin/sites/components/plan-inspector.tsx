"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { PersonSummaryDto, ReaderDirection, SecurityLevelAdminDto } from "@/lib/api/types";
import type { DraftArea, DraftDoor, DraftReader } from "../draft";
import { PointTable } from "./point-table";

function EntityError({ text }: { text: string | undefined }) {
  if (text === undefined) return null;
  return (
    <p role="alert" className="text-xs text-destructive" data-testid="entity-error">
      {text}
    </p>
  );
}

export function AreaInspector({
  area,
  error,
  securityLevels,
  searchEmployees,
  onChange,
  onDelete,
}: {
  area: DraftArea;
  error?: string;
  securityLevels: readonly SecurityLevelAdminDto[];
  searchEmployees: (query: string) => Promise<PersonSummaryDto[]>;
  onChange: (area: DraftArea) => void;
  onDelete: () => void;
}) {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<PersonSummaryDto[]>([]);
  const id = (name: string) => `area-${area.key}-${name}`;
  return (
    <section className="gh-panel flex flex-col gap-2" aria-label="Area" data-testid="area-inspector">
      <h3 className="gh-panel-title">Area</h3>
      <EntityError text={error} />
      <Label htmlFor={id("name")}>Area name</Label>
      <Input id={id("name")} value={area.name} onChange={(e) => onChange({ ...area, name: e.target.value })} />
      <Label htmlFor={id("code")}>Area code</Label>
      <Input id={id("code")} value={area.code} onChange={(e) => onChange({ ...area, code: e.target.value.toUpperCase() })} />
      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          checked={area.accessControlled ?? true}
          onChange={(e) => onChange({ ...area, accessControlled: e.target.checked })}
          aria-describedby={id("controlled-help")}
        />
        Access-controlled
      </label>
      <p id={id("controlled-help")} className="text-xs text-muted-foreground">
        Clear for a space without a PACS door (public corridor, storage). It needs no access-level mapping and cannot be requested.
      </p>
      <Label htmlFor={id("level")}>Security level</Label>
      <select
        id={id("level")}
        className="rounded border px-2 py-1"
        value={area.securityLevelId}
        onChange={(e) => onChange({ ...area, securityLevelId: e.target.value })}
      >
        {securityLevels.map((l) => (
          <option key={l.id} value={l.id}>
            {l.name}
          </option>
        ))}
      </select>
      <span className="text-sm">Owners</span>
      <ul className="flex flex-wrap gap-1 text-xs" data-testid="area-owners">
        {area.owners.map((o) => (
          <li key={o.personId} className="flex items-center gap-1 rounded-full border px-2 py-0.5">
            {o.displayName}
            <button
              type="button"
              aria-label={`Remove owner ${o.displayName}`}
              onClick={() => onChange({ ...area, owners: area.owners.filter((x) => x.personId !== o.personId) })}
            >
              ×
            </button>
          </li>
        ))}
        {area.owners.length === 0 && <li className="text-destructive">At least one owner is required.</li>}
      </ul>
      <div className="flex gap-1">
        <Input aria-label="Find owner" value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Employee name or email" />
        <Button
          type="button"
          size="sm"
          variant="outline"
          onClick={() => void searchEmployees(query).then(setResults, () => setResults([]))}
        >
          Find
        </Button>
      </div>
      {results.length > 0 && (
        <ul className="flex flex-col gap-1 text-xs">
          {results
            .filter((p) => !area.owners.some((o) => o.personId === p.personId))
            .map((p) => (
              <li key={p.personId}>
                <button
                  type="button"
                  className="underline"
                  onClick={() => onChange({ ...area, owners: [...area.owners, { personId: p.personId, displayName: p.displayName }] })}
                >
                  Add {p.displayName}
                </button>
              </li>
            ))}
        </ul>
      )}
      <PointTable
        label={area.name}
        points={area.polygon}
        onChange={(i, p) => onChange({ ...area, polygon: area.polygon.map((q, j) => (j === i ? p : q)) })}
        onRemove={(i) => onChange({ ...area, polygon: area.polygon.filter((_, j) => j !== i) })}
      />
      <Button type="button" variant="outline" size="sm" onClick={onDelete}>
        Delete area
      </Button>
    </section>
  );
}

export function DoorInspector({
  door,
  errors,
  onChange,
  onChangeReaders,
  onDelete,
  onAddReader,
  areas = [],
  areaKey,
  sharedWith = [],
  onMoveToArea,
}: {
  door: DraftDoor;
  errors: Record<string, string>;
  onChange: (door: DraftDoor) => void;
  onChangeReaders: (readers: DraftReader[]) => void;
  onDelete: () => void;
  onAddReader: () => void;
  /** Areas the door can belong to (lab finding #7). */
  areas?: { key: string; name: string }[];
  areaKey?: string;
  /** Names of the other areas whose wall the door lies on. */
  sharedWith?: string[];
  onMoveToArea?: (areaKey: string) => void;
}) {
  const id = (name: string) => `door-${door.key}-${name}`;
  const setReader = (key: string, patch: Partial<DraftReader>) =>
    onChangeReaders(door.readers.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  return (
    <section className="gh-panel flex flex-col gap-2" aria-label="Door" data-testid="door-inspector">
      <h3 className="gh-panel-title">Door</h3>
      <EntityError text={errors[door.key]} />
      <Label htmlFor={id("name")}>Door name</Label>
      <Input id={id("name")} value={door.name} onChange={(e) => onChange({ ...door, name: e.target.value })} />
      {areas.length > 0 && areaKey !== undefined && onMoveToArea !== undefined ? (
        <>
          <Label htmlFor={id("area")}>Belongs to area</Label>
          <select id={id("area")} className="rounded border px-2 py-1" value={areaKey} onChange={(e) => onMoveToArea(e.target.value)}>
            {areas.map((a) => (
              <option key={a.key} value={a.key}>
                {a.name}
              </option>
            ))}
          </select>
        </>
      ) : null}
      {sharedWith.length > 0 ? (
        <p role="status" className="text-xs text-amber-700 dark:text-amber-400" data-testid="shared-wall-warning">
          This door is on a wall shared with {sharedWith.join(", ")}. Access to the area it belongs to opens it; check the area above.
        </p>
      ) : null}
      <Label htmlFor={id("ref")}>PACS door reference</Label>
      <Input id={id("ref")} value={door.pacsDoorRef} onChange={(e) => onChange({ ...door, pacsDoorRef: e.target.value })} />
      <PointTable label={door.name} points={[door.position]} minPoints={1} onChange={(_, p) => onChange({ ...door, position: p })} />
      <span className="text-sm">Readers</span>
      <ul className="flex flex-col gap-2">
        {door.readers.map((r) => (
          <li key={r.key} className="flex flex-col gap-1 rounded border p-2" data-testid="reader-row">
            <EntityError text={errors[r.key]} />
            <Input aria-label={`Reader name ${r.name}`} value={r.name} onChange={(e) => setReader(r.key, { name: e.target.value })} />
            <div className="flex gap-1">
              <select
                aria-label={`Direction of ${r.name}`}
                className="rounded border px-1"
                value={r.direction}
                onChange={(e) => setReader(r.key, { direction: e.target.value as ReaderDirection })}
              >
                <option value="In">In</option>
                <option value="Out">Out</option>
              </select>
              <Input
                aria-label={`PACS reader reference of ${r.name}`}
                placeholder="PACS ref"
                value={r.pacsReaderRef}
                onChange={(e) => setReader(r.key, { pacsReaderRef: e.target.value })}
              />
            </div>
            <PointTable label={r.name} points={[r.position]} minPoints={1} onChange={(_, p) => setReader(r.key, { position: p })} />
            <button
              type="button"
              className="self-start text-xs underline"
              onClick={() => onChangeReaders(door.readers.filter((x) => x.key !== r.key))}
            >
              Remove reader
            </button>
          </li>
        ))}
      </ul>
      <div className="flex gap-2">
        <Button type="button" size="sm" variant="outline" onClick={onAddReader}>
          Add reader
        </Button>
        <Button type="button" size="sm" variant="outline" onClick={onDelete}>
          Delete door
        </Button>
      </div>
    </section>
  );
}
