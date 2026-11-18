import { randomBytes } from "node:crypto";
import type { SessionStore } from "./session-store";

export interface SessionData {
  sub: string;
  email: string;
  name: string;
  tenantId: string;
  accessToken: string;
  /** Epoch seconds. */
  accessTokenExpiresAt: number;
  refreshToken?: string;
  idToken?: string;
  /** Platform guest session from a magic link: no refresh token, never longer than the guest token. */
  guest?: boolean;
}

export const SESSION_TTL_SECONDS = 8 * 60 * 60;
export const GUEST_SESSION_TTL_SECONDS = 30 * 60;

/** Store TTL: 8 h sliding for normal sessions; guests never outlive their 30-minute token. */
export function sessionTtl(data: SessionData, nowSeconds = Math.floor(Date.now() / 1000)): number {
  if (data.guest !== true) {
    return SESSION_TTL_SECONDS;
  }
  return Math.max(1, Math.min(GUEST_SESSION_TTL_SECONDS, data.accessTokenExpiresAt - nowSeconds));
}

export function newSessionId(): string {
  return randomBytes(32).toString("base64url");
}

export function sessionCookieName(publicUrl: string): string {
  return publicUrl.startsWith("https://") ? "__Host-gh_session" : "gh_session";
}

export function sessionCookieOptions(publicUrl: string, maxAge: number = SESSION_TTL_SECONDS) {
  return {
    httpOnly: true,
    sameSite: "lax" as const,
    secure: publicUrl.startsWith("https://"),
    path: "/",
    maxAge,
  };
}

/**
 * Options that expire a session or binding cookie. A `__Host-` cookie is only replaced when the expiring Set-Cookie also carries
 * `Secure` and `Path=/`; a bare `cookies.delete(name)` is ignored by the browser, so the cookie would outlive the logout.
 */
export function expiredCookieOptions(publicUrl: string) {
  return { ...sessionCookieOptions(publicUrl, 0), expires: new Date(0) };
}

export async function createSession(store: SessionStore<SessionData>, data: SessionData): Promise<string> {
  const id = newSessionId();
  await store.set(id, data, sessionTtl(data));
  return id;
}

/** Reads a session and slides its expiry. Returns null when missing or expired. */
export async function readSession(store: SessionStore<SessionData>, id: string | undefined): Promise<SessionData | null> {
  if (id === undefined || id === "") {
    return null;
  }
  const data = await store.get(id);
  if (data === null) {
    return null;
  }
  return (await store.replace(id, data, sessionTtl(data))) ? data : null;
}

/** Stores a refreshed session; false when it was deleted meanwhile (logout), which is never undone. */
export async function updateSession(store: SessionStore<SessionData>, id: string, data: SessionData): Promise<boolean> {
  return store.replace(id, data, sessionTtl(data));
}
