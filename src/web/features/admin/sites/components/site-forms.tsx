"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError } from "@/lib/api/client";
import type { SaveFloorRequest, SaveSiteRequest } from "@/lib/api/types";
import { sitesAdminError } from "../api";

function timeZones(): string[] {
  try {
    return Intl.supportedValuesOf("timeZone");
  } catch {
    return ["UTC", "Europe/Copenhagen", "Europe/Oslo", "Europe/Stockholm", "Europe/Berlin", "Europe/London", "America/New_York"];
  }
}

export const errorOf = (error: unknown) =>
  error instanceof ApiError ? sitesAdminError(error.code, error.detail) : error instanceof Error ? error.message : null;

export function SiteForm({
  initial,
  submitLabel,
  onSubmit,
}: {
  initial?: SaveSiteRequest;
  submitLabel: string;
  onSubmit: (body: SaveSiteRequest) => Promise<unknown>;
}) {
  const [name, setName] = useState(initial?.name ?? "");
  const [code, setCode] = useState(initial?.code ?? "");
  const [zone, setZone] = useState(initial?.timeZoneId ?? Intl.DateTimeFormat().resolvedOptions().timeZone);
  const [error, setError] = useState<string | null>(null);
  return (
    <form
      className="grid gap-2 md:grid-cols-4"
      aria-label={submitLabel}
      onSubmit={(event) => {
        event.preventDefault();
        setError(null);
        onSubmit({ name: name.trim(), code: code.trim(), timeZoneId: zone }).then(
          () => {
            if (initial === undefined) {
              setName("");
              setCode("");
            }
          },
          (e: unknown) => setError(errorOf(e)),
        );
      }}
    >
      <div className="flex flex-col gap-1">
        <Label htmlFor={`site-name-${submitLabel}`}>Site name</Label>
        <Input id={`site-name-${submitLabel}`} required maxLength={100} value={name} onChange={(e) => setName(e.target.value)} />
      </div>
      <div className="flex flex-col gap-1">
        <Label htmlFor={`site-code-${submitLabel}`}>Site code</Label>
        <Input
          id={`site-code-${submitLabel}`}
          required
          pattern="[A-Z0-9-]{2,30}"
          value={code}
          onChange={(e) => setCode(e.target.value.toUpperCase())}
        />
      </div>
      <div className="flex flex-col gap-1">
        <Label htmlFor={`site-zone-${submitLabel}`}>Time zone</Label>
        <select id={`site-zone-${submitLabel}`} className="rounded border px-2 py-1" value={zone} onChange={(e) => setZone(e.target.value)}>
          {timeZones().map((z) => (
            <option key={z} value={z}>
              {z}
            </option>
          ))}
        </select>
      </div>
      <div className="flex items-end">
        <Button type="submit">{submitLabel}</Button>
      </div>
      {error !== null && (
        <p role="alert" className="text-sm text-destructive md:col-span-4">
          {error}
        </p>
      )}
    </form>
  );
}

export function FloorForm({
  initial,
  submitLabel,
  onSubmit,
}: {
  initial?: SaveFloorRequest;
  submitLabel: string;
  onSubmit: (body: SaveFloorRequest) => Promise<unknown>;
}) {
  const [name, setName] = useState(initial?.name ?? "");
  const [level, setLevel] = useState(String(initial?.level ?? 0));
  const [width, setWidth] = useState(String(initial?.planWidth ?? 1000));
  const [height, setHeight] = useState(String(initial?.planHeight ?? 600));
  const [error, setError] = useState<string | null>(null);
  const field = (id: string, label: string, value: string, set: (v: string) => void, type = "number") => (
    <div className="flex flex-col gap-1">
      <Label htmlFor={`${id}-${submitLabel}`}>{label}</Label>
      <Input id={`${id}-${submitLabel}`} type={type} required value={value} onChange={(e) => set(e.target.value)} />
    </div>
  );
  return (
    <form
      className="grid gap-2 md:grid-cols-5"
      aria-label={submitLabel}
      onSubmit={(event) => {
        event.preventDefault();
        setError(null);
        onSubmit({ name: name.trim(), level: Number(level), planWidth: Number(width), planHeight: Number(height) }).catch((e: unknown) =>
          setError(errorOf(e)),
        );
      }}
    >
      {field("floor-name", "Floor name", name, setName, "text")}
      {field("floor-level", "Level", level, setLevel)}
      {field("floor-width", "Plan width", width, setWidth)}
      {field("floor-height", "Plan height", height, setHeight)}
      <div className="flex items-end">
        <Button type="submit">{submitLabel}</Button>
      </div>
      {error !== null && (
        <p role="alert" className="text-sm text-destructive md:col-span-5">
          {error}
        </p>
      )}
    </form>
  );
}
