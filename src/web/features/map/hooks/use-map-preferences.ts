"use client";
import { useCallback, useSyncExternalStore } from "react";

export type MapMode = "spatial" | "plan" | "list";
export interface MapPreferences {
  siteId?: string;
  floorId?: string;
  desktopMode?: MapMode;
  mobileMode?: MapMode;
}
export function parseMapPreferences(raw: string | null): MapPreferences {
  try {
    const value: unknown = JSON.parse(raw ?? "{}");
    if (!value || typeof value !== "object") return {};
    const entry = value as Record<string, unknown>;
    const mode = (v: unknown): MapMode | undefined => (v === "spatial" || v === "plan" || v === "list" ? v : undefined);
    return {
      siteId: typeof entry.siteId === "string" ? entry.siteId : undefined,
      floorId: typeof entry.floorId === "string" ? entry.floorId : undefined,
      desktopMode: mode(entry.desktopMode),
      mobileMode: mode(entry.mobileMode),
    };
  } catch {
    return {};
  }
}
function subscribe(callback: () => void) {
  window.addEventListener("storage", callback);
  window.addEventListener("gatehouse-map-preferences", callback);
  return () => {
    window.removeEventListener("storage", callback);
    window.removeEventListener("gatehouse-map-preferences", callback);
  };
}
const serverSnapshot = () => null;
/** Save only display preferences, scoped to the signed-in tenant and subject. Storage can be disabled. */
export function useMapPreferences(tenantId?: string, subject?: string) {
  const key = tenantId && subject ? `gh:map:${encodeURIComponent(tenantId)}:${encodeURIComponent(subject)}` : null;
  const snapshot = useCallback(() => {
    try {
      return key ? localStorage.getItem(key) : null;
    } catch {
      return null;
    }
  }, [key]);
  const preferences = parseMapPreferences(useSyncExternalStore(subscribe, snapshot, serverSnapshot));
  const save = (patch: MapPreferences) => {
    if (!key) return;
    try {
      localStorage.setItem(key, JSON.stringify({ ...parseMapPreferences(localStorage.getItem(key)), ...patch }));
      window.dispatchEvent(new Event("gatehouse-map-preferences"));
    } catch {
      /* URL navigation works without storage. */
    }
  };
  return { preferences, save };
}
function subscribeMobile(callback: () => void) {
  const media = window.matchMedia?.("(max-width: 680px)");
  media?.addEventListener("change", callback);
  return () => media?.removeEventListener("change", callback);
}
export function useCompactScreen() {
  return useSyncExternalStore(
    subscribeMobile,
    () => window.matchMedia?.("(max-width: 680px)").matches ?? false,
    () => false,
  );
}
