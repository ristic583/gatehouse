"use client";

import { usePathname } from "next/navigation";
import type { ReactNode } from "react";

export type ShellKind = "platform" | "tenant";

/** The shell is chosen by route only (lab #23): `/platform/*` never shows the tenant chrome, whichever cookies exist. */
export function shellForPath(pathname: string | null): ShellKind {
  return pathname === "/platform" || pathname?.startsWith("/platform/") === true ? "platform" : "tenant";
}

/**
 * Wraps the tenant header/sidebar in the root layout. A client component because the root layout persists across client
 * navigation, so the route check must run on every pathname change, not only on the first server render.
 */
export function TenantShellSlot({ children }: { children: ReactNode }) {
  return shellForPath(usePathname()) === "platform" ? null : <>{children}</>;
}
