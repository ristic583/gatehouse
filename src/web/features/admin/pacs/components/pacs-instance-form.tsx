"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError } from "@/lib/api/client";
import type { PacsVendor, SavePacsInstanceRequest, SiteDto } from "@/lib/api/types";
import { VENDOR_TEXT } from "../api";

export const errorText = (e: unknown) =>
  e instanceof ApiError ? (e.code !== undefined ? `${e.detail} (${e.code})` : e.detail) : "The request failed.";

export function PacsInstanceForm({
  initial,
  sites,
  submitLabel,
  onSubmit,
}: {
  initial?: SavePacsInstanceRequest;
  sites: readonly SiteDto[];
  submitLabel: string;
  onSubmit: (body: SavePacsInstanceRequest) => Promise<unknown>;
}) {
  const [name, setName] = useState(initial?.name ?? "");
  const [vendor, setVendor] = useState<PacsVendor>(initial?.vendor ?? "LegacyPacs");
  const [siteIds, setSiteIds] = useState<string[]>(initial?.siteIds ?? []);
  const [error, setError] = useState<string | null>(null);
  return (
    <form
      className="flex flex-col gap-2"
      aria-label={submitLabel}
      onSubmit={(event) => {
        event.preventDefault();
        setError(null);
        onSubmit({ name: name.trim(), vendor, siteIds }).then(
          () => {
            if (initial === undefined) {
              setName("");
              setSiteIds([]);
            }
          },
          (e: unknown) => setError(errorText(e)),
        );
      }}
    >
      <div className="grid gap-2 md:grid-cols-2">
        <div className="flex flex-col gap-1">
          <Label htmlFor={`pacs-name-${submitLabel}`}>Instance name</Label>
          <Input id={`pacs-name-${submitLabel}`} required maxLength={100} value={name} onChange={(e) => setName(e.target.value)} />
        </div>
        <div className="flex flex-col gap-1">
          <Label htmlFor={`pacs-vendor-${submitLabel}`}>Vendor</Label>
          <select
            id={`pacs-vendor-${submitLabel}`}
            className="rounded border px-2 py-1"
            value={vendor}
            onChange={(e) => setVendor(e.target.value as PacsVendor)}
          >
            <option value="LegacyPacs">{VENDOR_TEXT.LegacyPacs}</option>
            <option value="CloudPacs">{VENDOR_TEXT.CloudPacs}</option>
          </select>
        </div>
      </div>
      <fieldset className="flex flex-wrap gap-3 text-sm">
        <legend className="mb-1">Sites</legend>
        {sites.map((s) => (
          <label key={s.siteId} className="flex items-center gap-1">
            <input
              type="checkbox"
              checked={siteIds.includes(s.siteId)}
              onChange={(e) => setSiteIds((ids) => (e.target.checked ? [...ids, s.siteId] : ids.filter((id) => id !== s.siteId)))}
            />{" "}
            {s.name}
          </label>
        ))}
        {sites.length === 0 && <span className="text-muted-foreground">Create a site first.</span>}
      </fieldset>
      {error !== null && (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}
      <Button type="submit" className="self-start">
        {submitLabel}
      </Button>
    </form>
  );
}
