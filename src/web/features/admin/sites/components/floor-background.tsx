"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { ApiError } from "@/lib/api/client";
import { sitesAdminApi, sitesAdminError } from "../api";

export const MAX_BACKGROUND_BYTES = 10 * 1024 * 1024;
const TYPES = ["image/png", "image/jpeg"];

/** Client pre-check mirroring the server (10 MiB, JPEG/PNG); null = acceptable. */
export function backgroundFileProblem(file: File): string | null {
  if (file.size > MAX_BACKGROUND_BYTES) return "The drawing is larger than 10 MiB.";
  if (!TYPES.includes(file.type)) return "Only JPEG and PNG drawings are accepted.";
  return null;
}

const opacityKey = (floorId: string) => `gh:floor-drawing-opacity:${floorId}`;

export function readOpacity(floorId: string): number {
  try {
    const value = Number(window.localStorage.getItem(opacityKey(floorId)));
    return Number.isFinite(value) && value > 0 && value <= 1 ? value : 0.5;
  } catch {
    return 0.5;
  }
}

export interface FloorBackgroundProps {
  floorId: string;
  hasBackground: boolean;
  opacity: number;
  onOpacityChange: (value: number) => void;
  onChanged: () => void;
  upload?: (floorId: string, file: File) => Promise<void>;
  remove?: (floorId: string) => Promise<void>;
}

/** Upload / replace / remove the floor drawing shown under the editor, the map and the live view. */
export function FloorBackground({
  floorId,
  hasBackground,
  opacity,
  onOpacityChange,
  onChanged,
  upload = sitesAdminApi.uploadBackground,
  remove = sitesAdminApi.removeBackground,
}: FloorBackgroundProps) {
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function run(action: () => Promise<void>) {
    setBusy(true);
    setError(null);
    try {
      await action();
      onChanged();
    } catch (e) {
      setError(e instanceof ApiError ? sitesAdminError(e.code, e.detail) : "The drawing could not be saved.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="gh-panel flex flex-col gap-2" aria-label="Floor drawing">
      <h3 className="gh-panel-title">Floor drawing</h3>
      <Label htmlFor={`floor-background-${floorId}`}>{hasBackground ? "Replace drawing" : "Upload drawing"} (JPEG/PNG, max 10 MiB)</Label>
      <input
        id={`floor-background-${floorId}`}
        data-testid="floor-background"
        type="file"
        accept="image/png,image/jpeg"
        disabled={busy}
        onChange={(event) => {
          const file = event.target.files?.[0];
          event.target.value = "";
          if (file === undefined) return;
          const problem = backgroundFileProblem(file);
          if (problem !== null) {
            setError(problem);
            return;
          }
          void run(() => upload(floorId, file));
        }}
      />
      {hasBackground && (
        <>
          <Label htmlFor={`floor-background-opacity-${floorId}`}>Drawing opacity</Label>
          <input
            id={`floor-background-opacity-${floorId}`}
            type="range"
            min={0.1}
            max={1}
            step={0.05}
            value={opacity}
            onChange={(e) => {
              const value = Number(e.target.value);
              onOpacityChange(value);
              try {
                window.localStorage.setItem(opacityKey(floorId), String(value));
              } catch {
                // Blocked storage only loses the preference.
              }
            }}
          />
          <Button type="button" size="sm" variant="outline" disabled={busy} onClick={() => void run(() => remove(floorId))}>
            Remove drawing
          </Button>
        </>
      )}
      {error !== null && (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}
    </section>
  );
}
