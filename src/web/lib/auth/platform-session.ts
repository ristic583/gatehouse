import { newSessionId } from "./session";
import type { SessionStore } from "./session-store";

/** Platform operator session (ADR-009 §1): separate cookie, Redis key and lifetime; never carries tenant claims. */
export interface PlatformSessionData {
  kind: "platform";
  sub: string;
  email: string;
  name: string;
  accessToken: string;
  /** Epoch seconds. */
  accessTokenExpiresAt: number;
  refreshToken?: string;
  idToken?: string;
  /** Epoch seconds; the session never outlives `createdAt + PLATFORM_SESSION_MAX_SECONDS`. */
  createdAt: number;
}

export const PLATFORM_SESSION_PREFIX = "web:platform-session:";
export const PLATFORM_AUTH_TX_PREFIX = "web:platform-authtx:";
export const PLATFORM_SESSION_MAX_SECONDS = 8 * 60 * 60;
export const PLATFORM_SESSION_IDLE_SECONDS = 30 * 60;

export function platformSessionCookieName(publicUrl: string): string {
  return publicUrl.startsWith("https://") ? "__Host-gh_platform_session" : "gh_platform_session";
}

export const PLATFORM_SESSION_COOKIES = ["gh_platform_session", "__Host-gh_platform_session"] as const;

/** Remaining store TTL: 30 min idle, capped by the absolute 8 h lifetime. 0 = expired. */
export function platformSessionTtl(data: PlatformSessionData, nowSeconds = Math.floor(Date.now() / 1000)): number {
  const remaining = data.createdAt + PLATFORM_SESSION_MAX_SECONDS - nowSeconds;
  return Math.max(0, Math.min(PLATFORM_SESSION_IDLE_SECONDS, remaining));
}

export async function createPlatformSession(store: SessionStore<PlatformSessionData>, data: PlatformSessionData): Promise<string> {
  const id = newSessionId();
  await store.set(id, data, Math.max(1, platformSessionTtl(data)));
  return id;
}

/** Reads a platform session and slides its idle expiry; null when missing, expired or not a platform session. */
export async function readPlatformSession(
  store: SessionStore<PlatformSessionData>,
  id: string | undefined,
  nowSeconds = Math.floor(Date.now() / 1000),
): Promise<PlatformSessionData | null> {
  if (id === undefined || id === "") {
    return null;
  }
  const data = await store.get(id);
  if (data === null || data.kind !== "platform") {
    return null;
  }
  const ttl = platformSessionTtl(data, nowSeconds);
  if (ttl <= 0) {
    await store.delete(id);
    return null;
  }
  return (await store.replace(id, data, ttl)) ? data : null;
}

export type ProxySessionKind = "tenant" | "platform";

/**
 * Which session a proxy path needs: `v1/platform/*` only with a platform session, everything else only with a
 * tenant session. Null = 401. Dot or empty segments are refused so the path cannot be normalised across the boundary.
 */
export function proxySessionFor(path: readonly string[], available: { tenant: boolean; platform: boolean }): ProxySessionKind | null {
  if (path.length === 0 || path.some((segment) => segment === "" || segment === "." || segment === "..")) {
    return null;
  }
  const isPlatform = path[0]?.toLowerCase() === "v1" && path[1]?.toLowerCase() === "platform";
  if (isPlatform) {
    return available.platform ? "platform" : null;
  }
  return available.tenant ? "tenant" : null;
}

/** Post-login targets of the platform BFF stay inside `/platform`. */
export function safePlatformReturnTo(value: string | null | undefined): string {
  if (value === null || value === undefined || !/^\/platform(\/|\?|$)/.test(value) || value.includes("://") || value.includes("\\")) {
    return "/platform";
  }
  return value;
}
