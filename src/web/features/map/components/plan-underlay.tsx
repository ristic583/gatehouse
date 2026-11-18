"use client";

import { useState } from "react";
import { useCompactScreen } from "../hooks/use-map-preferences";

/** BFF URL of a floor drawing; `updatedAt` busts the browser cache after a replace. */
export function floorBackgroundUrl(floorId: string, updatedAt: string | null | undefined): string {
  return `/api/proxy/v1/floors/${floorId}/background${updatedAt ? `?v=${encodeURIComponent(updatedAt)}` : ""}`;
}

/** "Show drawing": on by default, off by default on phones until the user turns it on. */
export function useShowDrawing(): [boolean, (value: boolean) => void] {
  const compact = useCompactScreen();
  const [choice, setChoice] = useState<boolean | null>(null);
  return [choice ?? !compact, setChoice];
}

export function ShowDrawingToggle({ checked, onChange }: { checked: boolean; onChange: (value: boolean) => void }) {
  return (
    <label className="flex items-center gap-1 text-xs">
      <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} /> Show drawing
    </label>
  );
}

/** Floor drawing stretched to the plan (ADR-007 SVG); rendered under the areas, ignored by pointer events. */
export function PlanUnderlay({ url, width, height, opacity = 0.45 }: { url: string; width: number; height: number; opacity?: number }) {
  return (
    <image
      href={url}
      x={0}
      y={0}
      width={width}
      height={height}
      preserveAspectRatio="none"
      opacity={opacity}
      pointerEvents="none"
      data-testid="plan-underlay"
    />
  );
}
